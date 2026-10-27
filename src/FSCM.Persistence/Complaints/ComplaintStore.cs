using FSCM.Application.Features.Complaints;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using FSCM.Application.Features.Deliveries;

namespace FSCM.Persistence.Complaints;

public sealed class ComplaintStore(AppDbContext db) : IComplaintStore
{
    public async Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.OrderCode) || string.IsNullOrWhiteSpace(data.Sku) || string.IsNullOrWhiteSpace(data.LabelCode))
            throw ComplaintOperationException.Validation("Order, SKU and label are required.");
        if (data.ActualQuantity < 0) throw ComplaintOperationException.Validation("Actual quantity cannot be negative.");
        var retailer = await db.RetailerRecords.SingleOrDefaultAsync(x => x.UserAccountId == actor && x.Status == "Active", ct)
            ?? throw ComplaintOperationException.Forbidden("The account is not linked to an active retailer.");
        var order = await db.SalesOrderRecords.SingleOrDefaultAsync(x => x.OrderCode == data.OrderCode && x.RetailerId == retailer.Id, ct)
            ?? throw ComplaintOperationException.NotFound("The delivered order was not found for this retailer.");
        var line = await (from l in db.SalesOrderLineRecords join p in db.ProductRecords on l.ProductId equals p.Id
                          where l.SalesOrderId == order.Id && p.Sku == data.Sku select new { Line = l, Product = p }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.NotFound("The SKU was not found in this order.");
        var receipt = await db.ReceiptConfirmationRecords.SingleOrDefaultAsync(x => x.SalesOrderId == order.Id && x.Status == ReceiptConfirmationStatuses.Confirmed, ct)
            ?? throw ComplaintOperationException.Conflict("The order has not been confirmed as received.");
        var shipment = await db.ShipmentRecords.SingleOrDefaultAsync(x => x.Id == receipt.ShipmentId && x.SalesOrderId == order.Id, ct)
            ?? throw ComplaintOperationException.Conflict("The receipt shipment part was not found.");
        var label = await (from l in db.BatchQrLabelRecords join b in db.BatchRecords on l.BatchId equals b.Id
                           where l.QrCode == data.LabelCode && b.ProductId == line.Product.Id select new { Label = l, Batch = b }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.NotFound("The label does not belong to this order SKU.");
        var receiptItem = await db.ReceiptConfirmationItemRecords.SingleOrDefaultAsync(x => x.ReceiptConfirmationId == receipt.Id && x.BatchQrLabelId == label.Label.Id, ct)
            ?? throw ComplaintOperationException.Conflict("The label was not recorded on the receipt.");
        if (data.ActualQuantity > receiptItem.ExpectedQuantity)
            throw ComplaintOperationException.Validation("Actual quantity cannot exceed the received expectation.");
        if (receipt.ShipmentId is not Guid shipmentId)
            throw ComplaintOperationException.Conflict("The receipt is not linked to a shipment part.");
        if (data.ShipmentId.HasValue && data.ShipmentId.Value != shipmentId)
            throw ComplaintOperationException.Validation("Shipment part does not belong to the receipt.");
        var priceRow = await (from split in db.RunPickingSplitRecords.AsNoTracking()
                              join gather in db.RunPickingGatherRecords.AsNoTracking() on split.RunPickingGatherId equals gather.Id
                              join price in db.AllocationPlanPriceRecords.AsNoTracking() on split.AllocationPlanPriceId equals price.Id
                              where gather.RunPickingListId == shipment.RunPickingListId
                                    && split.SalesOrderId == order.Id
                                    && split.SalesOrderLineId == receiptItem.SalesOrderLineId
                                    && gather.BatchId == receiptItem.BatchId
                              select new { price.Id, price.FinalNetUnitPrice }).SingleOrDefaultAsync(ct)
            ?? throw ComplaintOperationException.Conflict("The received batch is not linked to an allocation price.");
        if (data.AllocationPlanPriceId.HasValue && data.AllocationPlanPriceId.Value != priceRow.Id)
            throw ComplaintOperationException.Validation("Allocation price does not belong to the shipment batch.");
        var adjustment = Math.Round((receiptItem.ExpectedQuantity - data.ActualQuantity) * priceRow.FinalNetUnitPrice, 0,
            MidpointRounding.AwayFromZero);
        if (await db.ReceiptDiscrepancyRecords.AnyAsync(x => x.SalesOrderId == order.Id && x.ReceiptConfirmationId == receipt.Id && x.Status != "Resolved" && x.Note == data.Note, ct))
            throw ComplaintOperationException.Conflict("A matching complaint is already open.");
        var discrepancy = new ReceiptDiscrepancy { Id = Guid.NewGuid(), SalesOrderId = order.Id, ReceiptConfirmationId = receipt.Id,
            ReportedByRetailerUserId = actor, Status = "Open", Note = data.Note, ReportedAt = now };
        db.ReceiptDiscrepancyRecords.Add(discrepancy);
        db.ReceiptDiscrepancyItemRecords.Add(new ReceiptDiscrepancyItem { Id = Guid.NewGuid(), ReceiptDiscrepancyId = discrepancy.Id,
            BatchId = label.Batch.Id, ShipmentId = shipmentId, AllocationPlanPriceId = priceRow.Id,
            IssueType = data.ActualQuantity < receiptItem.ExpectedQuantity ? "Shortage" : "Damaged",
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
        if (row.d.Status == "Resolved") return ToResult(row.d, row.o.OrderCode, row.r.Code, row.r.Name, row.p.Sku, row.p.Name, row.i, null, row.i.EvidenceBlobUrl);
        row.d.Status = "Resolved"; row.d.ReviewedByUserId = actor; row.d.ReviewedAt = now; row.d.ResolvedAt = now; row.d.Note = data.Result;
        await db.SaveChangesAsync(ct);
        return ToResult(row.d, row.o.OrderCode, row.r.Code, row.r.Name, row.p.Sku, row.p.Name, row.i, null, row.i.EvidenceBlobUrl);
    }

    private static ComplaintResult ToResult(ReceiptDiscrepancy d, string order, string retailerCode, string retailerName, string sku, string product, ReceiptDiscrepancyItem i, string? label, string? evidence) =>
        new(d.Id, $"KN-{d.Id.ToString("N")[..8].ToUpperInvariant()}", order, retailerCode, retailerName, sku, product, i.ExpectedQuantity, i.ActualReceivedQuantity, i.IssueType, d.Status, label, i.Note ?? d.Note, evidence, d.ReportedAt, d.Status == "Resolved" ? d.Note : null, i.ShipmentId, i.AllocationPlanPriceId, i.FinalUnitPrice, i.AdjustmentAmount);
}
