using FSCM.Application.Features.Complaints;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using FSCM.Application.Features.Deliveries;

namespace FSCM.Persistence.Complaints;

public sealed class ComplaintStore(AppDbContext db) : IComplaintStore
{
    public async Task<IReadOnlyCollection<ComplaintTarget>> ListTargetsAsync(Guid actor, Guid orderId, CancellationToken ct)
    {
        var owned = await (from o in db.SalesOrderRecords join r in db.RetailerRecords on o.RetailerId equals r.Id
            where o.Id == orderId && r.UserAccountId == actor select o.Id).AnyAsync(ct);
        if (!owned) throw ComplaintOperationException.NotFound("Không tìm thấy đơn của cửa hàng.");
        var rows = await (from receipt in db.ReceiptConfirmationRecords
            join part in db.ShipmentRecords on receipt.ShipmentId equals part.Id
            join item in db.ReceiptConfirmationItemRecords on receipt.Id equals item.ReceiptConfirmationId
            join line in db.SalesOrderLineRecords on item.SalesOrderLineId equals line.Id
            join product in db.ProductRecords on line.ProductId equals product.Id
            join batch in db.BatchRecords on item.BatchId equals batch.Id
            join pick in db.PickingItemRecords on part.PickingListId equals pick.PickingListId
            join allocation in db.AllocationRecords on pick.AllocationId equals allocation.Id
            join link in db.ReceiptConfirmationQrLabelRecords on receipt.Id equals link.ReceiptConfirmationId
            join label in db.BatchQrLabelRecords on link.BatchQrLabelId equals label.Id
            where receipt.SalesOrderId == orderId && receipt.ShipmentId != null
                && (receipt.Status == ReceiptConfirmationStatuses.Confirmed || receipt.Status == ReceiptConfirmationStatuses.Exception)
                && allocation.SalesOrderLineId == line.Id && pick.SourceBatchId == item.BatchId && label.BatchId == item.BatchId
                && !db.ReceiptDiscrepancyItemRecords.Any(i => i.AllocationId == allocation.Id &&
                    db.ReceiptDiscrepancyRecords.Any(d => d.Id == i.ReceiptDiscrepancyId && d.Status != "Rejected"))
            select new ComplaintTarget(part.Id, part.ShipmentCode, allocation.Id, product.Sku,
                product.Name, batch.BatchCode, label.QrCode, item.ExpectedQuantity)).ToArrayAsync(ct);
        return rows.GroupBy(t => t.AllocationId).Select(g => g.OrderBy(t => t.LabelCode).First()).ToArray();
    }
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) { await operation(ct); return; }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Serializes complaint decisions and budgets, including the financial side effects.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(61811420261110)", ct);
            await operation(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, DateTimeOffset now, CancellationToken ct)
    {
        ReceiptDiscrepancy? result = null;
        await ExecuteInTransactionAsync(async token => result = await CreateCoreAsync(actor, data, now, token), ct);
        return result!;
    }

    private async Task<ReceiptDiscrepancy> CreateCoreAsync(Guid actor, CreateComplaintData data, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.OrderCode) || string.IsNullOrWhiteSpace(data.Sku) || string.IsNullOrWhiteSpace(data.LabelCode))
            throw ComplaintOperationException.Validation("Order, SKU and label are required.");
        if (data.ActualQuantity < 0) throw ComplaintOperationException.Validation("Actual quantity cannot be negative.");
        var issueType = string.IsNullOrWhiteSpace(data.IssueType)
            ? "Shortage"
            : data.IssueType.Trim() switch
            {
                "Shortage" => "Shortage",
                "Damaged" => "Damaged",
                _ => throw ComplaintOperationException.Validation(
                    "Issue type must be Shortage or Damaged.")
            };
        var retailer = await db.RetailerRecords.SingleOrDefaultAsync(x => x.UserAccountId == actor && x.Status == "Active", ct)
            ?? throw ComplaintOperationException.Forbidden("The account is not linked to an active retailer.");
        var order = await db.SalesOrderRecords.SingleOrDefaultAsync(x => x.OrderCode == data.OrderCode && x.RetailerId == retailer.Id, ct)
            ?? throw ComplaintOperationException.NotFound("The delivered order was not found for this retailer.");
        var line = await (from l in db.SalesOrderLineRecords join p in db.ProductRecords on l.ProductId equals p.Id
                          where l.SalesOrderId == order.Id && p.Sku == data.Sku select new { Line = l, Product = p }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.NotFound("The SKU was not found in this order.");
        var label = await (from l in db.BatchQrLabelRecords join b in db.BatchRecords on l.BatchId equals b.Id
                           where l.QrCode == data.LabelCode && b.ProductId == line.Product.Id select new { Label = l, Batch = b }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.NotFound("The label does not belong to this order SKU.");
        // A multi-warehouse order has one receipt per shipment part, and a part received with a
        // shortage or damage is itself an Exception receipt - exactly the case a claim exists for.
        // The receipt line is therefore found by order line + batch (+ shipment when given).
        var receiptRows = await (from item in db.ReceiptConfirmationItemRecords
                                 join r in db.ReceiptConfirmationRecords on item.ReceiptConfirmationId equals r.Id
                                 where r.SalesOrderId == order.Id
                                       && (r.Status == ReceiptConfirmationStatuses.Confirmed || r.Status == ReceiptConfirmationStatuses.Exception)
                                       && item.SalesOrderLineId == line.Line.Id && item.BatchId == label.Batch.Id
                                       && (!data.ShipmentId.HasValue || r.ShipmentId == data.ShipmentId.Value)
                                 select new { Receipt = r, Item = item }).ToArrayAsync(ct);
        if (receiptRows.Length == 0)
            throw ComplaintOperationException.Conflict("The batch was not recorded on a confirmed shipment receipt of this order.");
        if (receiptRows.Length > 1)
            throw ComplaintOperationException.Validation("Several shipment receipts match; specify the shipment part.");
        var receipt = receiptRows[0].Receipt;
        var receiptItem = receiptRows[0].Item;
        if (!await db.ReceiptConfirmationQrLabelRecords.AnyAsync(l => l.ReceiptConfirmationId == receipt.Id && l.BatchQrLabelId == label.Label.Id, ct))
            throw ComplaintOperationException.Validation("Tem không thuộc phiếu nhận của phần hàng này.");
        var shipment = await db.ShipmentRecords.SingleOrDefaultAsync(x => x.Id == receipt.ShipmentId && x.SalesOrderId == order.Id, ct)
            ?? throw ComplaintOperationException.Conflict("The receipt shipment part was not found.");        if (data.ActualQuantity > receiptItem.ExpectedQuantity)
            throw ComplaintOperationException.Validation("Actual quantity cannot exceed the received expectation.");
        if (data.ActualQuantity == receiptItem.ExpectedQuantity)
            throw ComplaintOperationException.Validation(
                "A complaint must identify at least one missing or unusable carton.");
        if (receipt.ShipmentId is not Guid shipmentId)
            throw ComplaintOperationException.Conflict("The receipt is not linked to a shipment part.");
        if (data.ShipmentId.HasValue && data.ShipmentId.Value != shipmentId)
            throw ComplaintOperationException.Validation("Shipment part does not belong to the receipt.");
        var priceRow = await (from item in db.PickingItemRecords.AsNoTracking()
                              join allocation in db.AllocationRecords.AsNoTracking() on item.AllocationId equals allocation.Id
                              where shipment.PickingListId.HasValue
                                    && item.PickingListId == shipment.PickingListId.Value
                                    && allocation.SalesOrderLineId == receiptItem.SalesOrderLineId
                                    && item.SourceBatchId == receiptItem.BatchId
                              select new
                              {
                                  allocation.Id,
                                  FinalNetUnitPrice = allocation.AllocatedQuantity == 0
                                      ? 0
                                      : allocation.NetAmount / allocation.AllocatedQuantity
                              }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.Conflict("The received batch is not linked to an allocation price.");
        var requestedAllocationId = data.AllocationId ?? data.AllocationPlanPriceId;
        if (requestedAllocationId.HasValue && requestedAllocationId.Value != priceRow.Id)
            throw ComplaintOperationException.Validation("Allocation does not belong to the shipment batch.");
        var adjustment = Math.Round((receiptItem.ExpectedQuantity - data.ActualQuantity) * priceRow.FinalNetUnitPrice, 0,
            MidpointRounding.AwayFromZero);
        var previous = await (from d in db.ReceiptDiscrepancyRecords
            join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
            where i.AllocationId == priceRow.Id && d.Status != "Rejected"
            select new { d.Status, i.ExpectedQuantity, i.ActualReceivedQuantity, i.AdjustmentAmount }).ToArrayAsync(ct);
        if (previous.Any(p => p.Status is "Submitted" or "UnderReview"))
            throw ComplaintOperationException.Conflict("Phần hàng này đang có khiếu nại chờ xử lý.");
        var affected = receiptItem.ExpectedQuantity - data.ActualQuantity;
        if (previous.Sum(p => p.ExpectedQuantity - p.ActualReceivedQuantity) + affected > receiptItem.ExpectedQuantity ||
            previous.Sum(p => p.AdjustmentAmount) + adjustment > Math.Round(receiptItem.ExpectedQuantity * priceRow.FinalNetUnitPrice, 0, MidpointRounding.AwayFromZero))
            throw ComplaintOperationException.Conflict("Tổng hàng/tiền khiếu nại vượt phần hàng đã giao.");
        if (previous.Length > 0)
            throw ComplaintOperationException.Conflict("Phần hàng này đã được khiếu nại; không thể gửi lại bằng ghi chú khác.");
        var discrepancy = new ReceiptDiscrepancy { Id = Guid.NewGuid(), SalesOrderId = order.Id, ReceiptConfirmationId = receipt.Id,
            ReportedByRetailerUserId = actor, Status = "Submitted", Note = data.Note, ReportedAt = now };
        db.ReceiptDiscrepancyRecords.Add(discrepancy);
        db.ReceiptDiscrepancyItemRecords.Add(new ReceiptDiscrepancyItem { Id = Guid.NewGuid(), ReceiptDiscrepancyId = discrepancy.Id,
            BatchId = label.Batch.Id, ShipmentId = shipmentId, AllocationId = priceRow.Id,
            IssueType = issueType,
            ExpectedQuantity = receiptItem.ExpectedQuantity, ActualReceivedQuantity = data.ActualQuantity,
            FinalUnitPrice = priceRow.FinalNetUnitPrice, AdjustmentAmount = adjustment,
            EvidenceBlobUrl = data.EvidenceUrl, Note = data.Note });
        await db.SaveChangesAsync(ct);
        return discrepancy;
    }

    public async Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actor, string role, CancellationToken ct)
    {
        var q = from d in db.ReceiptDiscrepancyRecords.AsNoTracking()
                join o in db.SalesOrderRecords.AsNoTracking() on d.SalesOrderId equals o.Id
                join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
                join i in db.ReceiptDiscrepancyItemRecords.AsNoTracking() on d.Id equals i.ReceiptDiscrepancyId
                join b in db.BatchRecords.AsNoTracking() on i.BatchId equals b.Id into batches
                from b in batches.DefaultIfEmpty()
                join p in db.ProductRecords.AsNoTracking() on b.ProductId equals p.Id into products
                from p in products.DefaultIfEmpty()
                join l in db.BatchQrLabelRecords.AsNoTracking() on b.Id equals l.BatchId into labels
                from l in labels.OrderByDescending(x => x.UpdatedAt).Take(1).DefaultIfEmpty()
                select new { d, o, r, i, p, l };
        if (role == SystemRoles.Retailer)
        {
            var retailerId = await db.RetailerRecords.Where(x => x.UserAccountId == actor).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
                ?? throw ComplaintOperationException.Forbidden("The account is not linked to a retailer.");
            q = q.Where(x => x.r.Id == retailerId);
        }
        var rows = await q.OrderByDescending(x => x.d.ReportedAt).ToArrayAsync(ct);
        return rows.Select(x => ToResult(x.d, x.o.OrderCode, x.r.Code, x.r.Name, x.p?.Sku ?? "", x.p?.Name ?? "", x.i, x.l?.QrCode, x.i.EvidenceBlobUrl)).ToArray();
    }

    public async Task<ComplaintResult> ResolveAsync(Guid id, Guid actor, ResolveComplaintData data, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.Result)) throw ComplaintOperationException.Validation("A resolution result is required.");
        var row = await (from d in db.ReceiptDiscrepancyRecords join o in db.SalesOrderRecords on d.SalesOrderId equals o.Id
                         join r in db.RetailerRecords on o.RetailerId equals r.Id join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
                         join b in db.BatchRecords on i.BatchId equals b.Id join p in db.ProductRecords on b.ProductId equals p.Id
                         where d.Id == id select new { d, o, r, i, p }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");
        if (row.d.Status is "Approved" or "Rejected" or "Resolved")
        {
            if ((row.d.Status == "Approved") != data.Approved)
                throw ComplaintOperationException.Conflict("Khiếu nại đã chốt kết quả, không thể đổi quyết định.");
            return ToResult(row.d, row.o.OrderCode, row.r.Code, row.r.Name, row.p.Sku, row.p.Name, row.i, null, row.i.EvidenceBlobUrl);
        }
        row.d.Status = data.Approved ? "Approved" : "Rejected";
        row.d.ReviewedByUserId = actor; row.d.ReviewedAt = now; row.d.ResolvedAt = now; row.d.Note = data.Result;
        await db.SaveChangesAsync(ct);
        return ToResult(row.d, row.o.OrderCode, row.r.Code, row.r.Name, row.p.Sku, row.p.Name, row.i, null, row.i.EvidenceBlobUrl);
    }

    public async Task MarkFinancialAdjustmentAppliedAsync(Guid id, CancellationToken ct)
    {
        var discrepancy = await db.ReceiptDiscrepancyRecords.SingleOrDefaultAsync(
            item => item.Id == id, ct)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");
        discrepancy.ResolutionType = "FinancialAdjustment";
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReceiptDiscrepancy?> FindOwnedComplaintAsync(Guid complaintId, Guid actorUserId, CancellationToken ct)
    {
        var complaint = await db.ReceiptDiscrepancyRecords.SingleOrDefaultAsync(x => x.Id == complaintId, ct);
        if (complaint is null) return null;
        // Retailer who reported it, or whose linked account owns it.
        if (complaint.ReportedByRetailerUserId == actorUserId) return complaint;
        var owns = await db.RetailerRecords
            .Join(db.SalesOrderRecords, r => r.Id, o => o.RetailerId, (r, o) => new { r.UserAccountId, o.Id })
            .AnyAsync(x => x.UserAccountId == actorUserId && x.Id == complaint.SalesOrderId, ct);
        return owns ? complaint : null;
    }

    public async Task AddEvidenceAsync(ComplaintEvidence evidence, CancellationToken ct)
    {
        db.ComplaintEvidenceRecords.Add(evidence);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyCollection<ComplaintEvidence>> ListEvidenceAsync(Guid complaintId, CancellationToken ct) =>
        await db.ComplaintEvidenceRecords.AsNoTracking()
            .Where(x => x.ReceiptDiscrepancyId == complaintId)
            .OrderBy(x => x.UploadedAt)
            .ToArrayAsync(ct);

    public Task<ComplaintEvidence?> FindEvidenceAsync(Guid complaintId, Guid evidenceId, CancellationToken ct) =>
        db.ComplaintEvidenceRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == evidenceId && x.ReceiptDiscrepancyId == complaintId, ct);

    private static ComplaintResult ToResult(ReceiptDiscrepancy d, string order, string retailerCode, string retailerName, string sku, string product, ReceiptDiscrepancyItem i, string? label, string? evidence) =>
        new(d.Id, $"KN-{d.Id.ToString("N")[..8].ToUpperInvariant()}", order, retailerCode, retailerName, sku, product, i.ExpectedQuantity, i.ActualReceivedQuantity, i.IssueType, d.Status is "Submitted" or "UnderReview" ? "Open" : d.Status, label, i.Note ?? d.Note, evidence, d.ReportedAt, d.Status is "Approved" or "Rejected" or "Resolved" ? d.Note : null, i.ShipmentId, i.AllocationPlanPriceId, i.FinalUnitPrice, i.AdjustmentAmount, d.SalesOrderId, i.AllocationId, d.ResolutionType == "FinancialAdjustment");
}

