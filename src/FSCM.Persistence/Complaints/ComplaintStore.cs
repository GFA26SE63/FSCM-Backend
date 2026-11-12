using FSCM.Application.Features.Complaints;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            select new ComplaintTarget(part.Id, part.ShipmentCode, allocation.Id, product.Sku,
                product.Name, batch.BatchCode, label.QrCode, item.ExpectedQuantity,
                item.ReceivedQuantity + item.DamagedQuantity)).ToArrayAsync(ct);
        var allocationIds = rows.Select(x => x.AllocationId).Distinct().ToArray();
        var prior = await (from d in db.ReceiptDiscrepancyRecords join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
            where i.AllocationId.HasValue && allocationIds.Contains(i.AllocationId.Value) && d.Status != "Rejected" && d.Status != "Cancelled"
            select new { i.AllocationId, SettlementMethod = d.ApprovedSettlementMethod ?? d.SettlementMethod, d.RequestType, d.RequiresCollection,
                Quantity = d.HandledQuantity ?? d.ApprovedQuantity ?? d.RequestedQuantity }).ToArrayAsync(ct);
        return rows.GroupBy(t => t.AllocationId).Select(g => g.OrderBy(t => t.LabelCode).First()).Select(t => t with {
            RemainingClaimableQuantity = Math.Max(0, t.ExpectedQuantity - prior.Where(p => p.AllocationId == t.AllocationId && p.SettlementMethod != "None").Sum(p => p.Quantity)),
            RemainingReturnableQuantity = Math.Max(0, t.ReceivedQuantity - prior.Where(p => p.AllocationId == t.AllocationId && (p.RequestType != "Complaint" || p.RequiresCollection)).Sum(p => p.Quantity))
        }).Where(t => t.RemainingClaimableQuantity > 0 || t.RemainingReturnableQuantity > 0).ToArray();
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
        if (data.RequestType is not ("Complaint" or "Return" or "DamagedCollection") ||
            data.SettlementMethod is not ("DebtOffset" or "CashRefund" or "None") ||
            data.SettlementMethod == "None" && data.RequestType != "DamagedCollection")
            throw ComplaintOperationException.Validation("Loại yêu cầu hoặc phương án bồi hoàn không hợp lệ.");
        if (data.Note?.Length > 2000) throw ComplaintOperationException.Validation("Mô tả tối đa 2000 ký tự.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data with { RequestId = null }))));
        if (data.RequestId.HasValue)
        {
            var priorRequest = await db.ReceiptDiscrepancyRecords.SingleOrDefaultAsync(d => d.ReportedByRetailerUserId == actor && d.RequestId == data.RequestId, ct);
            if (priorRequest is not null)
            {
                if (priorRequest.RequestHash != hash) throw ComplaintOperationException.Conflict("Mã yêu cầu đã dùng với nội dung khác.");
                return priorRequest;
            }
        }
        if (string.IsNullOrWhiteSpace(data.OrderCode) || string.IsNullOrWhiteSpace(data.Sku) || string.IsNullOrWhiteSpace(data.LabelCode))
            throw ComplaintOperationException.Validation("Order, SKU and label are required.");
        if (data.ActualQuantity < 0) throw ComplaintOperationException.Validation("Actual quantity cannot be negative.");
        if (data.RequestType != "Complaint" && string.IsNullOrWhiteSpace(data.Note))
            throw ComplaintOperationException.Validation("Vui lòng ghi rõ lý do trả/thu hồi hàng.");
        var issueType = data.RequestType == "Return" ? "Return" : data.RequestType == "DamagedCollection" ? "Damaged" : string.IsNullOrWhiteSpace(data.IssueType)
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
            ?? throw ComplaintOperationException.Conflict("The receipt shipment part was not found.");
        var affected = data.RequestType == "Complaint" ? receiptItem.ExpectedQuantity - data.ActualQuantity
            : data.AffectedQuantity ?? 0;
        if (affected <= 0 || decimal.Truncate(affected) != affected)
            throw ComplaintOperationException.Validation("Số thùng yêu cầu phải là số nguyên lớn hơn 0.");
        var actual = receiptItem.ExpectedQuantity - affected;
        if (actual < 0 || data.ActualQuantity > receiptItem.ExpectedQuantity)
            throw ComplaintOperationException.Validation("Actual quantity cannot exceed the received expectation.");
        if (actual == receiptItem.ExpectedQuantity)
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
        var adjustment = data.SettlementMethod == "None" ? 0 : Math.Round(affected * priceRow.FinalNetUnitPrice, 0,
            MidpointRounding.AwayFromZero);
        var previous = await (from d in db.ReceiptDiscrepancyRecords
            join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
            where i.AllocationId == priceRow.Id && d.Status != "Rejected" && d.Status != "Cancelled"
            select new { d.Status, d.RequestType, d.RequiresCollection, SettlementMethod = d.ApprovedSettlementMethod ?? d.SettlementMethod, i.AdjustmentAmount,
                Quantity = d.HandledQuantity ?? d.ApprovedQuantity ?? d.RequestedQuantity }).ToArrayAsync(ct);
        if (data.RequestType == "Complaint" && !data.RequestId.HasValue && previous.Any())
            throw ComplaintOperationException.Conflict("Phần hàng này đã có yêu cầu. Vui lòng kiểm tra trước khi gửi lại.");
        var needsCollection = data.RequiresCollection || data.RequestType == "DamagedCollection";
        if (issueType == "Shortage" && data.RequestType == "Complaint" && needsCollection)
            throw ComplaintOperationException.Validation("Hàng giao thiếu không có hàng để thu hồi.");
        var physical = data.RequestType != "Complaint" || needsCollection;
        if (physical && affected + previous.Where(p => p.RequestType != "Complaint" || p.RequiresCollection).Sum(p => p.Quantity) > receiptItem.ReceivedQuantity + receiptItem.DamagedQuantity)
            throw ComplaintOperationException.Conflict("Số thùng trả/thu hồi vượt phần hàng đã nhận còn đủ điều kiện.");
        if (data.SettlementMethod != "None" && (previous.Where(p => p.SettlementMethod != "None").Sum(p => p.Quantity) + affected > receiptItem.ExpectedQuantity ||
            previous.Sum(p => p.AdjustmentAmount) + adjustment > Math.Round(receiptItem.ExpectedQuantity * priceRow.FinalNetUnitPrice, 0, MidpointRounding.AwayFromZero)))
            throw ComplaintOperationException.Conflict("Tổng hàng/tiền khiếu nại vượt phần hàng đã giao.");
        var discrepancy = new ReceiptDiscrepancy { Id = Guid.NewGuid(), SalesOrderId = order.Id, ReceiptConfirmationId = receipt.Id,
            ReportedByRetailerUserId = actor, Status = "Submitted", Note = data.Note, ReportedAt = now,
            RequestType = data.RequestType, SettlementMethod = data.SettlementMethod, RequestedQuantity = affected,
            RequiresCollection = needsCollection, RequestId = data.RequestId, RequestHash = hash };
        db.ReceiptDiscrepancyRecords.Add(discrepancy);
        db.ReceiptDiscrepancyItemRecords.Add(new ReceiptDiscrepancyItem { Id = Guid.NewGuid(), ReceiptDiscrepancyId = discrepancy.Id,
            BatchId = label.Batch.Id, ShipmentId = shipmentId, AllocationId = priceRow.Id,
            IssueType = issueType,
            ExpectedQuantity = receiptItem.ExpectedQuantity, ActualReceivedQuantity = actual,
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
                from l in labels.Where(l => db.ReceiptConfirmationQrLabelRecords.Any(link => link.ReceiptConfirmationId == d.ReceiptConfirmationId && link.BatchQrLabelId == l.Id))
                    .OrderByDescending(x => x.UpdatedAt).Take(1).DefaultIfEmpty()
                select new { d, o, r, i, p, l };
        if (role == SystemRoles.Retailer)
        {
            var retailerId = await db.RetailerRecords.Where(x => x.UserAccountId == actor).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
                ?? throw ComplaintOperationException.Forbidden("The account is not linked to a retailer.");
            q = q.Where(x => x.r.Id == retailerId);
        }
        var rows = await q.OrderByDescending(x => x.d.ReportedAt).ToArrayAsync(ct);
        var ids = rows.Select(x => x.d.Id).ToArray();
        var documents = await db.ReturnHandlingDocumentRecords.AsNoTracking().Where(x => ids.Contains(x.ReceiptDiscrepancyId)).ToArrayAsync(ct);
        var requests = (await db.AuditLogRecords.AsNoTracking()
            .Where(a => a.EntityName == nameof(ReceiptDiscrepancy) && a.Action == "ComplaintEvidenceRequested" && ids.Contains(a.EntityId))
            .OrderByDescending(a => a.CreatedAt).ToArrayAsync(ct))
            .GroupBy(a => a.EntityId).ToDictionary(g => g.Key, g => g.First().Reason);
        return rows.Select(x => ToResult(x.d, x.o.OrderCode, x.r.Code, x.r.Name, x.p?.Sku ?? "", x.p?.Name ?? "", x.i, x.l?.QrCode, x.i.EvidenceBlobUrl,
            x.d.Status == "UnderReview" ? requests.GetValueOrDefault(x.d.Id) : null) with {
                Documents = documents.Where(d => d.ReceiptDiscrepancyId == x.d.Id).Select(DocumentResult).ToArray()
            }).ToArray();
    }

    public async Task<ComplaintResult> ResolveAsync(Guid id, Guid actor, ResolveComplaintData data, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.Result)) throw ComplaintOperationException.Validation("A resolution result is required.");
        var row = await (from d in db.ReceiptDiscrepancyRecords join o in db.SalesOrderRecords on d.SalesOrderId equals o.Id
                         join r in db.RetailerRecords on o.RetailerId equals r.Id join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
                         join b in db.BatchRecords on i.BatchId equals b.Id join p in db.ProductRecords on b.ProductId equals p.Id
                         where d.Id == id select new { d, o, r, i, p }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");
        if (row.d.Status == "Cancelled")
            throw ComplaintOperationException.Conflict("Khiếu nại đã được Retailer hủy.");
        if (row.d.Status is "Approved" or "Rejected" or "Resolved")
        {
            if ((row.d.Status != "Rejected") != data.Approved)
                throw ComplaintOperationException.Conflict("Khiếu nại đã chốt kết quả, không thể đổi quyết định.");
            if (data.ApprovedQuantity.HasValue && data.ApprovedQuantity != row.d.ApprovedQuantity ||
                data.SettlementMethod is not null && data.SettlementMethod != row.d.ApprovedSettlementMethod)
                throw ComplaintOperationException.Conflict("Yêu cầu đã được duyệt với phương án khác.");
            return await ResultAsync(id, ct);
        }
        if (data.ExpectedVersion.HasValue && data.ExpectedVersion != row.d.Version)
            throw ComplaintOperationException.Conflict("Yêu cầu đã thay đổi, hãy tải lại trước khi xử lý.");
        if (row.d.RequestType != "Complaint" && !data.ExpectedVersion.HasValue)
            throw ComplaintOperationException.Validation("Cần phiên bản hiện tại của yêu cầu.");
        var approvedQuantity = data.ApprovedQuantity ?? row.d.RequestedQuantity;
        var settlement = data.SettlementMethod ?? row.d.SettlementMethod;
        if (data.Approved && (approvedQuantity <= 0 || decimal.Truncate(approvedQuantity) != approvedQuantity || approvedQuantity > row.d.RequestedQuantity))
            throw ComplaintOperationException.Validation("Số thùng được duyệt phải nguyên, lớn hơn 0 và không vượt số yêu cầu.");
        if (settlement is not ("DebtOffset" or "CashRefund" or "None") || settlement == "None" && row.d.RequestType != "DamagedCollection")
            throw ComplaintOperationException.Validation("Phương án bồi hoàn không hợp lệ.");
        if (data.WaivePhysicalHandling && string.IsNullOrWhiteSpace(data.WaiverReason))
            throw ComplaintOperationException.Validation("Miễn xử lý hàng thực tế phải có lý do.");
        if (data.Result.Length > 2000 || data.WaiverReason?.Length > 1000)
            throw ComplaintOperationException.Validation("Ghi chú tối đa 2000 ký tự, lý do miễn xử lý tối đa 1000 ký tự.");
        // Changing a collection-only request to compensation must reserve the financial budget too.
        if (data.Approved && settlement != "None")
        {
            var otherQuantities = await (from d in db.ReceiptDiscrepancyRecords
                join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
                where d.Id != id && i.AllocationId == row.i.AllocationId && d.Status != "Rejected" && d.Status != "Cancelled"
                    && (d.ApprovedSettlementMethod ?? d.SettlementMethod) != "None"
                select d.HandledQuantity ?? d.ApprovedQuantity ?? d.RequestedQuantity).ToArrayAsync(ct);
            if (otherQuantities.Sum() + approvedQuantity > row.i.ExpectedQuantity)
                throw ComplaintOperationException.Conflict("Phần hàng không còn đủ số lượng được bồi hoàn; hãy tải lại các yêu cầu liên quan.");
        }
        var oldStatus = row.d.Status;
        row.d.Status = data.Approved ? "Approved" : "Rejected";
        row.d.ReviewedByUserId = actor; row.d.ReviewedAt = now;
        row.d.ResolvedAt = data.Approved ? null : now; row.d.Note = data.Result.Trim(); row.d.Version++;
        // An approved claim is settled in money only: the receivable is adjusted and any amount the
        // retailer already paid is refunded through a manual refund record (F101).
        if (data.Approved)
        {
            row.d.ApprovedQuantity = approvedQuantity;
            row.d.ApprovedSettlementMethod = settlement;
            row.i.AdjustmentAmount = settlement == "None" ? 0 : Money(approvedQuantity * row.i.FinalUnitPrice);
            row.d.ResolutionType = settlement == "None" ? null : "FinancialAdjustment";
            var physical = row.d.RequestType != "Complaint" || row.d.RequiresCollection;
            row.d.PhysicalHandlingCompleted = !physical || data.WaivePhysicalHandling;
            if (row.d.PhysicalHandlingCompleted) row.d.HandledQuantity = approvedQuantity;
            if (physical)
            {
                void AddDocument(string kind, string prefix) => db.ReturnHandlingDocumentRecords.Add(new() {
                    Id = Guid.NewGuid(), ReceiptDiscrepancyId = id, Kind = kind,
                    Code = $"{prefix}-{id:N}", ApprovedQuantity = approvedQuantity, CreatedAt = now,
                    Status = data.WaivePhysicalHandling ? "Waived" : "Pending",
                    ActualQuantity = data.WaivePhysicalHandling ? approvedQuantity : null,
                    CompletedAt = data.WaivePhysicalHandling ? now : null,
                    CompletedByUserId = data.WaivePhysicalHandling ? actor : null,
                    Note = data.WaivePhysicalHandling ? data.WaiverReason?.Trim() : null
                });
                if (row.d.RequestType == "Return" || settlement != "None") AddDocument("ReturnOrder", "RT");
                if (row.d.RequiresCollection) AddDocument("CollectionOrder", "TH");
            }
        }
        Audit(actor, data.Approved ? "ApproveClaimFinancial" : "RejectClaim", row.d, oldStatus, data.Result, now);
        await db.SaveChangesAsync(ct);
        return await ResultAsync(id, ct);
    }

    public async Task MarkFinancialAdjustmentAppliedAsync(Guid id, CancellationToken ct)
    {
        var discrepancy = await db.ReceiptDiscrepancyRecords.SingleOrDefaultAsync(
            item => item.Id == id, ct)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");
        discrepancy.ResolutionType = "FinancialAdjustment";
        discrepancy.FinancialAdjustmentApplied = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ComplaintResult> CompleteHandlingAsync(Guid id, Guid actor, CompleteRequestHandlingData data, DateTimeOffset now, CancellationToken ct)
    {
        var completedAt = new DateTimeOffset(data.CompletedAt.UtcTicks - data.CompletedAt.UtcTicks % 10, TimeSpan.Zero);
        var d = await db.ReceiptDiscrepancyRecords.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ComplaintOperationException.NotFound("Không tìm thấy yêu cầu.");
        var docs = await db.ReturnHandlingDocumentRecords.Where(x => x.ReceiptDiscrepancyId == id).ToArrayAsync(ct);
        if (d.PhysicalHandlingCompleted)
        {
            if (d.HandledQuantity != data.ActualQuantity || docs.Any(x => x.Status != "Completed" || x.Note != data.Note.Trim() || x.EvidenceReference != data.EvidenceReference.Trim() || x.CompletedAt != completedAt))
                throw ComplaintOperationException.Conflict("Yêu cầu đã chốt xử lý với nội dung khác.");
            return await ResultAsync(id, ct);
        }
        if (d.Status != "Approved" || docs.Length == 0)
            throw ComplaintOperationException.Conflict("Chỉ xác nhận phiếu đã được Admin duyệt và đang chờ xử lý hàng.");
        if (d.Version != data.ExpectedVersion) throw ComplaintOperationException.Conflict("Yêu cầu đã thay đổi, hãy tải lại.");
        if (data.ActualQuantity < 0 || decimal.Truncate(data.ActualQuantity) != data.ActualQuantity || data.ActualQuantity > d.ApprovedQuantity)
            throw ComplaintOperationException.Validation("Số thùng xử lý thực tế phải nguyên, từ 0 đến số được duyệt.");
        if (string.IsNullOrWhiteSpace(data.Note) || data.Note.Length > 1000 || string.IsNullOrWhiteSpace(data.EvidenceReference) || data.EvidenceReference.Length > 2000)
            throw ComplaintOperationException.Validation("Cần ghi chú và bằng chứng xử lý thực tế (tối đa 1000/2000 ký tự).");
        if (completedAt > now || completedAt < d.ReviewedAt)
            throw ComplaintOperationException.Validation("Ngày xử lý phải sau lúc duyệt và không ở tương lai.");
        foreach (var doc in docs)
        {
            doc.Status = "Completed"; doc.ActualQuantity = data.ActualQuantity; doc.CompletedAt = completedAt;
            doc.CompletedByUserId = actor; doc.Note = data.Note.Trim(); doc.EvidenceReference = data.EvidenceReference.Trim();
        }
        d.HandledQuantity = data.ActualQuantity; d.PhysicalHandlingCompleted = true; d.Version++;
        var item = await db.ReceiptDiscrepancyItemRecords.SingleAsync(i => i.ReceiptDiscrepancyId == id, ct);
        item.AdjustmentAmount = d.ApprovedSettlementMethod == "None" ? 0 : Money(data.ActualQuantity * item.FinalUnitPrice);
        Audit(actor, "RequestHandlingCompleted", d, d.Status, data.Note, now);
        await db.SaveChangesAsync(ct);
        return await ResultAsync(id, ct);
    }

    public async Task RecordSettlementAsync(Guid id, decimal debtOffset, decimal refundAmount, Guid? refundId, DateTimeOffset now, CancellationToken ct)
    {
        var d = await db.ReceiptDiscrepancyRecords.SingleAsync(x => x.Id == id, ct);
        d.DebtOffsetAmount = debtOffset; d.RefundAmount = refundAmount; d.RefundId = refundId;
        d.FinancialAdjustmentApplied = d.ApprovedSettlementMethod != "None";
        d.Status = "Resolved"; d.ResolvedAt = now; d.Version++;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ComplaintResult> ResultAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(Guid.Empty, SystemRoles.Admin, ct)).Single(x => x.Id == id);
    private static decimal Money(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);
    private static ReturnHandlingDocumentResult DocumentResult(ReturnHandlingDocument d) =>
        new(d.Id, d.Code, d.Kind, d.Status, d.ApprovedQuantity, d.ActualQuantity, d.CompletedAt, d.Note, d.EvidenceReference);

    public async Task CancelAsync(Guid id, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var d = await FindOwnedComplaintAsync(id, actor, ct)
            ?? throw ComplaintOperationException.NotFound("Không tìm thấy khiếu nại của bạn.");
        if (d.Status == "Cancelled") return;
        if (d.Status is not ("Submitted" or "UnderReview"))
            throw ComplaintOperationException.Conflict("Khiếu nại đã có kết quả, không thể hủy.");
        var old = d.Status;
        // ResolvedAt closes the claim for the completion gate and frees the batch for a new claim.
        d.Status = "Cancelled"; d.ResolvedAt = now; d.Version++;
        Audit(actor, "CancelClaim", d, old, "Retailer hủy khiếu nại", now);
        await db.SaveChangesAsync(ct);
    }

    public async Task RequestEvidenceAsync(Guid id, Guid actor, string reason, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw ComplaintOperationException.Validation("A reason is required to request more evidence.");
        var d = await db.ReceiptDiscrepancyRecords.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");
        if (d.Status is not ("Submitted" or "UnderReview"))
            throw ComplaintOperationException.Conflict("Khiếu nại đã có kết quả, không thể yêu cầu bổ sung.");
        var old = d.Status;
        d.Status = "UnderReview"; d.ReviewedByUserId = actor; d.Version++;
        Audit(actor, "ComplaintEvidenceRequested", d, old, reason.Trim(), now);
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

    public async Task<string?> RemoveEvidenceAsync(
        Guid complaintId, Guid evidenceId, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var evidence = await db.ComplaintEvidenceRecords.SingleOrDefaultAsync(
            x => x.Id == evidenceId && x.ReceiptDiscrepancyId == complaintId, ct);
        if (evidence is null) return null;
        var claim = await db.ReceiptDiscrepancyRecords.SingleAsync(x => x.Id == complaintId, ct);
        db.ComplaintEvidenceRecords.Remove(evidence);
        Audit(actor, "ComplaintEvidenceRemoved", claim, claim.Status, evidence.FileName, now);
        await db.SaveChangesAsync(ct);
        return evidence.BlobKey;
    }

    public async Task<IReadOnlyCollection<ComplaintEvidence>> ListEvidenceAsync(Guid complaintId, CancellationToken ct) =>
        await db.ComplaintEvidenceRecords.AsNoTracking()
            .Where(x => x.ReceiptDiscrepancyId == complaintId)
            .OrderBy(x => x.UploadedAt)
            .ToArrayAsync(ct);

    public Task<ComplaintEvidence?> FindEvidenceAsync(Guid complaintId, Guid evidenceId, CancellationToken ct) =>
        db.ComplaintEvidenceRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == evidenceId && x.ReceiptDiscrepancyId == complaintId, ct);

    private static ComplaintResult ToResult(ReceiptDiscrepancy d, string order, string retailerCode, string retailerName, string sku, string product, ReceiptDiscrepancyItem i, string? label, string? evidence, string? evidenceRequest = null) =>
        new ComplaintResult(d.Id, $"KN-{d.Id.ToString("N")[..8].ToUpperInvariant()}", order, retailerCode, retailerName, sku, product, i.ExpectedQuantity, i.ActualReceivedQuantity, i.IssueType, d.Status is "Submitted" or "UnderReview" ? "Open" : d.Status, label, i.Note ?? d.Note, evidence, d.ReportedAt, d.Status is "Approved" or "Rejected" or "Resolved" ? d.Note : null, i.ShipmentId, i.AllocationPlanPriceId, i.FinalUnitPrice, i.AdjustmentAmount, d.SalesOrderId, i.AllocationId, d.ResolutionType == "FinancialAdjustment",
            d.ResolutionType, d.Status, evidenceRequest) with {
                FinancialAdjustmentApplied = d.FinancialAdjustmentApplied,
                RequestType = d.RequestType, SettlementMethod = d.SettlementMethod, ApprovedSettlementMethod = d.ApprovedSettlementMethod,
                RequestedQuantity = d.RequestedQuantity, ApprovedQuantity = d.ApprovedQuantity, HandledQuantity = d.HandledQuantity,
                RequiresCollection = d.RequiresCollection, PhysicalHandlingCompleted = d.PhysicalHandlingCompleted,
                DebtOffsetAmount = d.DebtOffsetAmount, RefundAmount = d.RefundAmount, RefundId = d.RefundId, Version = d.Version
            };

    private void Audit(Guid? actor, string action, ReceiptDiscrepancy d, string? oldStatus, string? reason, DateTimeOffset now) =>
        db.AuditLogRecords.Add(new AuditLog
        {
            Id = Guid.NewGuid(), UserAccountId = actor, EntityName = nameof(ReceiptDiscrepancy), EntityId = d.Id,
            Action = action,
            OldValues = System.Text.Json.JsonSerializer.Serialize(new { Status = oldStatus }),
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { d.Status, d.ResolutionType, d.RequestType,
                d.SettlementMethod, d.ApprovedSettlementMethod, d.RequestedQuantity, d.ApprovedQuantity,
                d.HandledQuantity, d.PhysicalHandlingCompleted, d.Version }),
            Reason = reason, CreatedAt = now
        });
}
