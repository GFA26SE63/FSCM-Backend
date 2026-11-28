using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Application.Features.Reports;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

public sealed partial class BusinessReportStore
{
    public async Task<FulfillmentReportResult> GetFulfillmentAsync(
        FulfillmentReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var (start, end) = PeriodBounds(q.FromDate, q.ToDate);
        var isSalesManager = actorRole == SystemRoles.SalesManager;

        HashSet<Guid>? groupSet = null;
        HashSet<Guid>? whSet = null;
        string scope;

        if (isSalesManager)
        {
            if (q.Tab is not ("shipments" or "drivers"))
                throw ReportOperationException.Forbidden("Quản lý kinh doanh chỉ được xem tiến độ giao hàng (OTIF) của đơn hàng thuộc nhóm.");
            if (q.WarehouseId.HasValue)
                throw ReportOperationException.Forbidden("Quản lý kinh doanh không lọc trực tiếp theo kho nội bộ.");
            (groupSet, scope) = await ResolveGroupScopeAsync(null, actorUserId, actorRole, ct);
        }
        else if (actorRole == SystemRoles.WarehouseKeeper)
        {
            (whSet, scope) = await ResolveWarehouseScopeAsync(q.WarehouseId, actorUserId, actorRole, ct);
        }
        else if (actorRole is SystemRoles.Admin or SystemRoles.Operator)
        {
            whSet = q.WarehouseId.HasValue ? [q.WarehouseId.Value] : null;
            scope = q.WarehouseId.HasValue ? "Kho đã chọn" : "Toàn hệ thống";
        }
        else
        {
            throw ReportOperationException.Forbidden("Vai trò này không có quyền xem báo cáo vận hành SLA.");
        }

        // Load Shipments (visible for all allowed roles, scoped properly)
        var orderEtas = db.SalesOrderLineRecords.AsNoTracking()
            .GroupBy(l => l.SalesOrderId)
            .Select(g => new { SalesOrderId = g.Key, Eta = g.Max(x => x.EstimatedArrivalAt) });

        var rawShipments = await (
            from sh in db.ShipmentRecords.AsNoTracking()
            where sh.CreatedAt >= start && sh.CreatedAt < end
                || (sh.ReceivedAt.HasValue && sh.ReceivedAt.Value >= start && sh.ReceivedAt.Value < end)
            join o in db.SalesOrderRecords.AsNoTracking() on sh.SalesOrderId equals o.Id
            join wh in db.WarehouseRecords.AsNoTracking() on sh.WarehouseId equals wh.Id
            join eta in orderEtas on sh.SalesOrderId equals eta.SalesOrderId into etaRows
            from eta in etaRows.DefaultIfEmpty()
            where !q.RegionId.HasValue || wh.RegionId == q.RegionId.Value
            select new
            {
                sh.Id,
                sh.ShipmentCode,
                sh.SalesOrderId,
                o.OrderCode,
                o.SalesGroupId,
                sh.WarehouseId,
                WarehouseCode = wh.Code,
                WarehouseName = wh.Name,
                o.RetailerCodeSnapshot,
                o.RetailerNameSnapshot,
                sh.DriverId,
                sh.DriverCodeSnapshot,
                sh.DriverNameSnapshot,
                sh.PlateNumberSnapshot,
                sh.Status,
                sh.TotalQuantity,
                sh.ReceivedQuantity,
                sh.ReadyAt,
                sh.IssuedAt,
                sh.DispatchedAt,
                sh.ReceivedAt,
                Eta = eta != null ? eta.Eta : null,
                sh.ExceptionReason
            }).ToArrayAsync(ct);

        if (groupSet != null) rawShipments = rawShipments.Where(s => groupSet.Contains(s.SalesGroupId)).ToArray();
        if (whSet != null) rawShipments = rawShipments.Where(s => whSet.Contains(s.WarehouseId)).ToArray();

        var searchTerm = q.Search?.Trim().ToLowerInvariant();
        var shipmentsMapped = rawShipments.Select(s =>
        {
            bool? onTime = s.ReceivedAt.HasValue && s.Eta.HasValue ? s.ReceivedAt.Value <= s.Eta.Value : null;
            decimal? transitHours = s.DispatchedAt.HasValue && s.ReceivedAt.HasValue
                ? Math.Round((decimal)(s.ReceivedAt.Value - s.DispatchedAt.Value).TotalHours, 1) : null;
            return new FulfillmentShipmentItem(
                s.Id, s.ShipmentCode, s.SalesOrderId, s.OrderCode, s.WarehouseCode, s.WarehouseName,
                s.RetailerCodeSnapshot, s.RetailerNameSnapshot, s.DriverNameSnapshot, s.PlateNumberSnapshot,
                s.Status, s.TotalQuantity, s.ReceivedQuantity, Math.Max(0m, s.TotalQuantity - s.ReceivedQuantity),
                s.ReadyAt, s.IssuedAt, s.DispatchedAt, s.ReceivedAt, s.Eta, onTime, transitHours, s.ExceptionReason);
        }).ToArray();

        var deliveredShipments = shipmentsMapped.Where(s => s.ReceivedAt.HasValue).ToArray();
        var totalSentQty = deliveredShipments.Sum(s => s.TotalQuantity);
        var totalRecvQty = deliveredShipments.Sum(s => s.ReceivedQuantity);
        var timedDelivered = deliveredShipments.Where(s => s.IsOnTime.HasValue).ToArray();

        int? receiptCount = null;
        decimal? receiptCartons = null;
        int? pickingCount = null;
        int? pickingExceptionCount = null;
        int? transferCount = null;
        int? transferInTransitCount = null;

        BusinessPagedList<FulfillmentShipmentItem> shipmentsPage = EmptyPage<FulfillmentShipmentItem>(q.Page, q.PageSize);
        BusinessPagedList<FulfillmentDriverItem> driversPage = EmptyPage<FulfillmentDriverItem>(q.Page, q.PageSize);
        BusinessPagedList<FulfillmentInboundItem> inboundPage = EmptyPage<FulfillmentInboundItem>(q.Page, q.PageSize);
        BusinessPagedList<FulfillmentPickingItem> pickingPage = EmptyPage<FulfillmentPickingItem>(q.Page, q.PageSize);
        BusinessPagedList<FulfillmentTransferItem> transfersPage = EmptyPage<FulfillmentTransferItem>(q.Page, q.PageSize);

        if (!isSalesManager)
        {
            // Warehouse SLA stats (Inbound, Picking, Transfers)
            var wdInboundRaw = await (
                from d in db.WarehouseDocuments.AsNoTracking()
                where d.Kind == "Receipt" && d.Reason == "Factory" && d.CreatedAt >= start && d.CreatedAt < end
                join wh in db.WarehouseRecords.AsNoTracking() on d.WarehouseId equals wh.Id
                join reg in db.RegionRecords.AsNoTracking() on wh.RegionId equals reg.Id into regRows
                from reg in regRows.DefaultIfEmpty()
                where !q.RegionId.HasValue || wh.RegionId == q.RegionId.Value
                select new
                {
                    d.Id,
                    d.Code,
                    d.CreatedAt,
                    d.PostedAt,
                    d.WarehouseId,
                    WarehouseCode = wh.Code,
                    WarehouseName = wh.Name,
                    RegionName = reg != null ? reg.Name : "",
                    d.Status,
                    d.CreatedByUserId,
                    d.Note,
                    LineCount = db.WarehouseDocumentLines.Count(l => l.DocumentId == d.Id),
                    Qty = db.WarehouseDocumentLines.Where(l => l.DocumentId == d.Id).Sum(l => (decimal?)l.Quantity) ?? 0m
                }).ToArrayAsync(ct);

            var legacyGoodsReceipts = await (
                from gr in db.GoodsReceiptRecords.AsNoTracking()
                where gr.CreatedAt >= start && gr.CreatedAt < end
                    && !db.WarehouseDocuments.Any(d => d.GoodsReceiptId == gr.Id)
                join wh in db.WarehouseRecords.AsNoTracking() on gr.WarehouseId equals wh.Id
                join reg in db.RegionRecords.AsNoTracking() on wh.RegionId equals reg.Id into regRows
                from reg in regRows.DefaultIfEmpty()
                where !q.RegionId.HasValue || wh.RegionId == q.RegionId.Value
                select new
                {
                    gr.Id,
                    Code = gr.ReceiptCode,
                    gr.CreatedAt,
                    PostedAt = gr.ApprovedAt ?? gr.ReceivedAt,
                    gr.WarehouseId,
                    WarehouseCode = wh.Code,
                    WarehouseName = wh.Name,
                    RegionName = reg != null ? reg.Name : "",
                    gr.Status,
                    gr.CreatedByUserId,
                    Note = gr.RejectionReason,
                    LineCount = db.GoodsReceiptLineRecords.Count(l => l.GoodsReceiptId == gr.Id),
                    Qty = db.GoodsReceiptLineRecords.Where(l => l.GoodsReceiptId == gr.Id).Sum(l => (decimal?)l.ReceivedQuantity) ?? 0m
                }).ToArrayAsync(ct);

            if (whSet != null)
            {
                wdInboundRaw = wdInboundRaw.Where(x => whSet.Contains(x.WarehouseId)).ToArray();
                legacyGoodsReceipts = legacyGoodsReceipts.Where(x => whSet.Contains(x.WarehouseId)).ToArray();
            }

            receiptCount = wdInboundRaw.Length + legacyGoodsReceipts.Length;
            receiptCartons = wdInboundRaw.Sum(x => x.Qty) + legacyGoodsReceipts.Sum(x => x.Qty);

            var pickingRaw = await (
                from pl in db.PickingListRecords.AsNoTracking()
                where pl.CreatedAt >= start && pl.CreatedAt < end
                join po in db.PurchaseOrderRecords.AsNoTracking() on pl.PurchaseOrderId equals po.Id
                join o in db.SalesOrderRecords.AsNoTracking() on po.SalesOrderId equals o.Id
                join wh in db.WarehouseRecords.AsNoTracking() on pl.WarehouseId equals wh.Id
                where !q.RegionId.HasValue || wh.RegionId == q.RegionId.Value
                select new
                {
                    pl.Id,
                    pl.PickingCode,
                    o.SalesGroupId,
                    SalesOrderId = o.Id,
                    o.OrderCode,
                    pl.WarehouseId,
                    WarehouseCode = wh.Code,
                    WarehouseName = wh.Name,
                    pl.Status,
                    pl.CreatedAt,
                    pl.StartedAt,
                    pl.ConfirmedAt,
                    pl.AssignedToUserId,
                    pl.ConfirmedByUserId,
                    LineCount = db.PickingItemRecords.Count(i => i.PickingListId == pl.Id),
                    ReqQty = db.PickingItemRecords.Where(i => i.PickingListId == pl.Id).Sum(i => (decimal?)i.RequestedQuantity) ?? 0m,
                    PickQty = db.PickingItemRecords.Where(i => i.PickingListId == pl.Id).Sum(i => (decimal?)i.PickedQuantity) ?? 0m
                }).ToArrayAsync(ct);

            if (whSet != null) pickingRaw = pickingRaw.Where(p => whSet.Contains(p.WarehouseId)).ToArray();
            pickingCount = pickingRaw.Length;
            pickingExceptionCount = pickingRaw.Count(p => p.Status == "Exception");

            var transferRaw = await (
                from wt in db.WarehouseTransferRecords.AsNoTracking()
                where wt.RequestedAt >= start && wt.RequestedAt < end
                join src in db.WarehouseRecords.AsNoTracking() on wt.SourceWarehouseId equals src.Id
                join dst in db.WarehouseRecords.AsNoTracking() on wt.DestinationWarehouseId equals dst.Id
                where !q.RegionId.HasValue || src.RegionId == q.RegionId.Value || dst.RegionId == q.RegionId.Value
                select new
                {
                    wt.Id,
                    wt.TransferCode,
                    wt.SourceWarehouseId,
                    SourceCode = src.Code,
                    SourceName = src.Name,
                    wt.DestinationWarehouseId,
                    DestCode = dst.Code,
                    DestName = dst.Name,
                    wt.Status,
                    wt.DriverNameSnapshot,
                    wt.PlateNumberSnapshot,
                    wt.RequestedAt,
                    wt.DispatchedAt,
                    wt.ReceivedAt,
                    wt.Note,
                    ReqQty = db.WarehouseTransferItemRecords.Where(i => i.WarehouseTransferId == wt.Id).Sum(i => (decimal?)i.RequestedQuantity) ?? 0m,
                    DispQty = db.WarehouseTransferItemRecords.Where(i => i.WarehouseTransferId == wt.Id).Sum(i => (decimal?)i.DispatchedQuantity) ?? 0m,
                    RecvQty = db.WarehouseTransferItemRecords.Where(i => i.WarehouseTransferId == wt.Id).Sum(i => (decimal?)i.ReceivedQuantity) ?? 0m,
                }).ToArrayAsync(ct);

            if (whSet != null)
                transferRaw = transferRaw.Where(t => whSet.Contains(t.SourceWarehouseId) || whSet.Contains(t.DestinationWarehouseId)).ToArray();

            transferCount = transferRaw.Length;
            transferInTransitCount = transferRaw.Count(t => t.Status == "InTransit");

            if (q.Tab == "inbound")
            {
                var creatorNames = await LoadUserNamesAsync(
                    wdInboundRaw.Select(x => (Guid?)x.CreatedByUserId).Concat(legacyGoodsReceipts.Select(x => (Guid?)x.CreatedByUserId)), ct);
                var combined = wdInboundRaw.Select(x => new FulfillmentInboundItem(
                        x.Id, x.Code, "Phiếu nhập nhà máy (PNK)", x.CreatedAt, x.PostedAt,
                        x.WarehouseCode, x.WarehouseName, x.RegionName, x.LineCount, x.Qty, x.Status,
                        x.PostedAt.HasValue ? Math.Round((decimal)(x.PostedAt.Value - x.CreatedAt).TotalHours, 1) : null,
                        creatorNames.GetValueOrDefault(x.CreatedByUserId), x.Note))
                    .Concat(legacyGoodsReceipts.Select(x => new FulfillmentInboundItem(
                        x.Id, x.Code, "Goods Receipt", x.CreatedAt, x.PostedAt,
                        x.WarehouseCode, x.WarehouseName, x.RegionName, x.LineCount, x.Qty, x.Status,
                        x.PostedAt.HasValue ? Math.Round((decimal)(x.PostedAt.Value - x.CreatedAt).TotalHours, 1) : null,
                        creatorNames.GetValueOrDefault(x.CreatedByUserId), x.Note)))
                    .Where(x => string.IsNullOrWhiteSpace(q.Status) || x.Status == q.Status)
                    .Where(x => string.IsNullOrWhiteSpace(searchTerm) ||
                        x.DocumentCode.ToLowerInvariant().Contains(searchTerm) ||
                        x.WarehouseCode.ToLowerInvariant().Contains(searchTerm) ||
                        x.WarehouseName.ToLowerInvariant().Contains(searchTerm))
                    .OrderByDescending(x => x.CreatedAt)
                    .ToArray();
                inboundPage = Paginate(combined, q.Page, q.PageSize);
            }
            else if (q.Tab == "picking")
            {
                var pickUsers = await LoadUserNamesAsync(
                    pickingRaw.SelectMany(p => new[] { p.AssignedToUserId, p.ConfirmedByUserId }), ct);
                var items = pickingRaw
                    .Where(p => string.IsNullOrWhiteSpace(q.Status) || p.Status == q.Status)
                    .Where(p => string.IsNullOrWhiteSpace(searchTerm) ||
                        p.PickingCode.ToLowerInvariant().Contains(searchTerm) ||
                        p.OrderCode.ToLowerInvariant().Contains(searchTerm) ||
                        p.WarehouseCode.ToLowerInvariant().Contains(searchTerm))
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p =>
                    {
                        decimal? mins = p.StartedAt.HasValue && p.ConfirmedAt.HasValue
                            ? Math.Round((decimal)(p.ConfirmedAt.Value - p.StartedAt.Value).TotalMinutes, 1)
                            : p.ConfirmedAt.HasValue
                                ? Math.Round((decimal)(p.ConfirmedAt.Value - p.CreatedAt).TotalMinutes, 1) : null;
                        return new FulfillmentPickingItem(
                            p.Id, p.PickingCode, p.SalesOrderId, p.OrderCode, p.WarehouseCode, p.WarehouseName,
                            p.Status, p.LineCount, p.ReqQty, p.PickQty, p.CreatedAt, p.StartedAt, p.ConfirmedAt, mins,
                            p.AssignedToUserId.HasValue ? pickUsers.GetValueOrDefault(p.AssignedToUserId.Value) : null,
                            p.ConfirmedByUserId.HasValue ? pickUsers.GetValueOrDefault(p.ConfirmedByUserId.Value) : null);
                    }).ToArray();
                pickingPage = Paginate(items, q.Page, q.PageSize);
            }
            else if (q.Tab == "transfers")
            {
                var items = transferRaw
                    .Where(t => string.IsNullOrWhiteSpace(q.Status) || t.Status == q.Status)
                    .Where(t => string.IsNullOrWhiteSpace(searchTerm) ||
                        t.TransferCode.ToLowerInvariant().Contains(searchTerm) ||
                        t.SourceCode.ToLowerInvariant().Contains(searchTerm) ||
                        t.DestCode.ToLowerInvariant().Contains(searchTerm))
                    .OrderByDescending(t => t.RequestedAt)
                    .Select(t =>
                    {
                        decimal? transitHours = t.DispatchedAt.HasValue && t.ReceivedAt.HasValue
                            ? Math.Round((decimal)(t.ReceivedAt.Value - t.DispatchedAt.Value).TotalHours, 1) : null;
                        return new FulfillmentTransferItem(
                            t.Id, t.TransferCode, t.SourceCode, t.SourceName, t.DestCode, t.DestName,
                            t.Status, t.DriverNameSnapshot, t.PlateNumberSnapshot,
                            t.ReqQty, t.DispQty, t.RecvQty, t.RequestedAt, t.DispatchedAt, t.ReceivedAt, transitHours, t.Note);
                    }).ToArray();
                transfersPage = Paginate(items, q.Page, q.PageSize);
            }
        }

        if (q.Tab == "shipments")
        {
            var filtered = shipmentsMapped
                .Where(s => string.IsNullOrWhiteSpace(q.Status) || s.Status == q.Status)
                .Where(s => string.IsNullOrWhiteSpace(searchTerm) ||
                    s.ShipmentCode.ToLowerInvariant().Contains(searchTerm) ||
                    s.OrderCode.ToLowerInvariant().Contains(searchTerm) ||
                    s.RetailerCode.ToLowerInvariant().Contains(searchTerm) ||
                    s.RetailerName.ToLowerInvariant().Contains(searchTerm) ||
                    (s.DriverName ?? "").ToLowerInvariant().Contains(searchTerm) ||
                    (s.PlateNumber ?? "").ToLowerInvariant().Contains(searchTerm))
                .OrderByDescending(s => s.ReceivedAt ?? s.DispatchedAt ?? s.ReadyAt)
                .ThenBy(s => s.ShipmentCode)
                .ToArray();
            shipmentsPage = Paginate(filtered, q.Page, q.PageSize);
        }
        else if (q.Tab == "drivers")
        {
            var groupedDrivers = rawShipments
                .Where(s => s.DriverId.HasValue)
                .GroupBy(s => new
                {
                    DriverId = s.DriverId!.Value,
                    Code = s.DriverCodeSnapshot ?? "—",
                    Name = s.DriverNameSnapshot ?? "Chưa rõ tài xế",
                    Plate = s.PlateNumberSnapshot ?? "—",
                    s.WarehouseCode
                })
                .Select(g =>
                {
                    var sent = g.Sum(x => x.TotalQuantity);
                    var recv = g.Sum(x => x.ReceivedQuantity);
                    var timed = g.Where(x => x.ReceivedAt.HasValue && x.Eta.HasValue).ToArray();
                    var onTime = timed.Count(x => x.ReceivedAt!.Value <= x.Eta!.Value);
                    return new FulfillmentDriverItem(
                        g.Key.DriverId, g.Key.Code, g.Key.Name, g.Key.Plate, g.Key.WarehouseCode,
                        g.Count(),
                        g.Count(x => x.Status == ShipmentStatuses.Received),
                        g.Count(x => x.Status == ShipmentStatuses.Exception),
                        onTime,
                        sent, recv,
                        sent <= 0 ? 0m : Math.Round(recv / sent * 100m, 1),
                        timed.Length == 0 ? 0m : Math.Round((decimal)onTime / timed.Length * 100m, 1));
                })
                .Where(d => string.IsNullOrWhiteSpace(searchTerm) ||
                    d.DriverCode.ToLowerInvariant().Contains(searchTerm) ||
                    d.DriverName.ToLowerInvariant().Contains(searchTerm) ||
                    d.PlateNumber.ToLowerInvariant().Contains(searchTerm))
                .OrderByDescending(d => d.TripCount).ThenBy(d => d.DriverName)
                .ToArray();
            driversPage = Paginate(groupedDrivers, q.Page, q.PageSize);
        }

        var summary = new FulfillmentSummary(
            shipmentsMapped.Length,
            deliveredShipments.Length,
            shipmentsMapped.Count(s => s.Status == ShipmentStatuses.Exception),
            totalSentQty <= 0 ? 0m : Math.Round(totalRecvQty / totalSentQty * 100m, 1),
            timedDelivered.Length == 0 ? 0m : Math.Round((decimal)timedDelivered.Count(s => s.IsOnTime == true) / timedDelivered.Length * 100m, 1),
            Math.Max(0m, totalSentQty - totalRecvQty),
            receiptCount,
            receiptCartons,
            pickingCount,
            pickingExceptionCount,
            transferCount,
            transferInTransitCount);

        string[] notes =
        [
            "OTIF (On-Time In-Full) tính dựa trên ngày nhận thực tế của Retailer (ReceivedAt) so với ETA của đơn hàng và tỷ lệ thùng xác nhận nhận / xuất.",
            isSalesManager
                ? "Phạm vi Quản lý kinh doanh hiển thị tiến độ giao hàng và năng suất tài xế phục vụ các đơn thuộc nhóm bán hàng quản lý."
                : "Bao gồm đầy đủ luồng vận hành kho: Nhập kho nhà máy (PNK), Soạn hàng (Picking), Luân chuyển nội bộ và Giao vận.",
        ];

        return new FulfillmentReportResult(
            q.FromDate, q.ToDate, q.Tab, scope, summary, shipmentsPage, driversPage, inboundPage, pickingPage, transfersPage, notes);
    }

    public async Task<InventoryRiskReportResult> GetInventoryRiskAsync(
        InventoryRiskReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var (start, end) = PeriodBounds(q.FromDate, q.ToDate);
        var today = TodayVietnam();
        var (whSet, scope) = await ResolveWarehouseScopeAsync(q.WarehouseId, actorUserId, actorRole, ct);
        var searchTerm = q.Search?.Trim().ToLowerInvariant();

        var nowUtc = timeProvider.GetUtcNow();
        var effectiveTable = await db.ShelfLifeDiscountTableRecords.AsNoTracking()
            .Where(t => t.IsActive && t.EffectiveFrom <= nowUtc && (t.EffectiveTo == null || t.EffectiveTo > nowUtc))
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        var activeTiers = effectiveTable is null
            ? []
            : await db.ShelfLifeDiscountTierRecords.AsNoTracking()
                .Where(t => t.ShelfLifeDiscountTableId == effectiveTable.Id && t.DiscountValue > 0)
                .ToArrayAsync(ct);

        // 1. Batch expiry snapshot (Expired + NearExpiry)
        var batchRows = await (
            from batch in db.BatchRecords.AsNoTracking()
            join bal in db.InventoryBalanceRecords.AsNoTracking() on batch.Id equals bal.BatchId
            join wh in db.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals wh.Id
            join reg in db.RegionRecords.AsNoTracking() on wh.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            join prod in db.ProductRecords.AsNoTracking() on batch.ProductId equals prod.Id
            where bal.PhysicalQuantity > 0
                && (!q.RegionId.HasValue || wh.RegionId == q.RegionId.Value)
            let locked = db.InventoryLockRecords
                .Where(l => l.BatchId == batch.Id && l.Status == InventoryLockStatuses.Active)
                .Sum(l => (decimal?)l.LockedQuantity) ?? 0m
            let unitPrice = db.ProductPriceRecords
                .Where(pr => pr.ProductId == prod.Id && pr.IsActive)
                .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault() ?? 0m
            select new
            {
                batch.Id,
                batch.BatchCode,
                batch.WarehouseId,
                WarehouseCode = wh.Code,
                WarehouseName = wh.Name,
                RegionName = reg != null ? reg.Name : "",
                ProductId = prod.Id,
                prod.ProductCategoryId,
                prod.Sku,
                ProductName = prod.Name,
                prod.NearDateThresholdHours,
                batch.ProductionDate,
                batch.ExpirationDate,
                bal.PhysicalQuantity,
                bal.ReservedQuantity,
                LockedQuantity = locked,
                UnitPrice = unitPrice
            }).ToArrayAsync(ct);

        if (whSet != null)
            batchRows = batchRows.Where(b => whSet.Contains(b.WarehouseId)).ToArray();

        bool HasShelfLifeDiscount(Guid productId, Guid categoryId, DateOnly prodDate, DateOnly expDate)
        {
            if (activeTiers.Length == 0) return false;
            var totalDays = expDate.DayNumber - prodDate.DayNumber;
            if (totalDays <= 0) return false;
            var remainingDays = expDate.DayNumber - today.DayNumber;
            if (remainingDays <= 0) return false;
            var remainingPct = Math.Clamp(remainingDays * 100m / totalDays, 0m, 100m);
            var matched = activeTiers
                .Where(t => remainingPct >= t.MinRemainingPercent && remainingPct < t.MaxRemainingPercent)
                .ToArray();
            return matched.Any(t => t.Scope == "Sku" && t.ProductId == productId)
                || matched.Any(t => t.Scope == "Category" && t.ProductCategoryId == categoryId);
        }

        var expiryBatches = batchRows
            .Select(b =>
            {
                var expired = b.ExpirationDate < today;
                var near = !expired && WarehouseLogisticsReportBuilder.IsNearExpiry(b.ExpirationDate, b.NearDateThresholdHours, today);
                if (!expired && !near) return null;
                var available = expired ? 0m : Math.Max(0m, b.PhysicalQuantity - b.ReservedQuantity - b.LockedQuantity);
                var atRiskQty = Math.Max(0m, b.PhysicalQuantity - b.ReservedQuantity);
                var hasPromo = !expired && HasShelfLifeDiscount(b.ProductId, b.ProductCategoryId, b.ProductionDate, b.ExpirationDate);
                return new ExpiryActionBatchItem(
                    b.Id, b.BatchCode, b.WarehouseId, b.WarehouseCode, b.WarehouseName, b.RegionName,
                    b.Sku, b.ProductName, b.ProductionDate, b.ExpirationDate,
                    b.ExpirationDate.DayNumber - today.DayNumber,
                    expired ? "Expired" : "NearExpiry",
                    b.PhysicalQuantity, b.ReservedQuantity, b.LockedQuantity, available,
                    hasPromo, b.UnitPrice, atRiskQty * b.UnitPrice);
            })
            .Where(b => b != null)
            .Select(b => b!)
            .ToArray();

        // 2. Inventory locks (all active locks + locks created/released in period)
        var lockRows = await (
            from l in db.InventoryLockRecords.AsNoTracking()
            join wh in db.WarehouseRecords.AsNoTracking() on l.WarehouseId equals wh.Id
            join reg in db.RegionRecords.AsNoTracking() on wh.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            join batch in db.BatchRecords.AsNoTracking() on l.BatchId equals batch.Id
            join prod in db.ProductRecords.AsNoTracking() on l.ProductId equals prod.Id
            where (l.Status == InventoryLockStatuses.Active
                    || (l.CreatedAt >= start && l.CreatedAt < end)
                    || (l.ReleasedAt >= start && l.ReleasedAt < end))
                && (!q.RegionId.HasValue || wh.RegionId == q.RegionId.Value)
            let unitPrice = db.ProductPriceRecords
                .Where(pr => pr.ProductId == prod.Id && pr.EffectiveFrom <= l.CreatedAt && (pr.EffectiveTo == null || pr.EffectiveTo > l.CreatedAt))
                .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault()
                ?? db.ProductPriceRecords.Where(pr => pr.ProductId == prod.Id && pr.IsActive)
                    .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault() ?? 0m
            select new
            {
                l.Id,
                l.WarehouseId,
                WarehouseCode = wh.Code,
                WarehouseName = wh.Name,
                RegionName = reg != null ? reg.Name : "",
                batch.BatchCode,
                prod.Sku,
                ProductName = prod.Name,
                batch.ExpirationDate,
                l.LockedQuantity,
                l.Reason,
                l.RequiresResolution,
                l.Status,
                l.LockedByUserId,
                l.ReleasedByUserId,
                l.CreatedAt,
                l.ReleasedAt,
                Value = l.LockedQuantity * unitPrice,
                l.Note
            }).ToArrayAsync(ct);

        if (whSet != null)
            lockRows = lockRows.Where(l => whSet.Contains(l.WarehouseId)).ToArray();

        // 3. Write-offs in period
        var disposalRows = await (
            from line in db.WriteOffItemRecords.AsNoTracking()
            join doc in db.WriteOffRecords.AsNoTracking() on line.WriteOffId equals doc.Id
            join wh in db.WarehouseRecords.AsNoTracking() on doc.WarehouseId equals wh.Id
            join reg in db.RegionRecords.AsNoTracking() on wh.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            join batch in db.BatchRecords.AsNoTracking() on line.BatchId equals batch.Id
            join prod in db.ProductRecords.AsNoTracking() on batch.ProductId equals prod.Id
            where doc.CreatedAt >= start && doc.CreatedAt < end
                && (!q.RegionId.HasValue || wh.RegionId == q.RegionId.Value)
            let unitPrice = db.ProductPriceRecords
                .Where(pr => pr.ProductId == prod.Id && pr.EffectiveFrom <= doc.CreatedAt && (pr.EffectiveTo == null || pr.EffectiveTo > doc.CreatedAt))
                .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault()
                ?? db.ProductPriceRecords.Where(pr => pr.ProductId == prod.Id && pr.IsActive)
                    .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault() ?? 0m
            select new
            {
                line.Id,
                WriteOffId = doc.Id,
                doc.WriteOffCode,
                doc.CreatedAt,
                doc.WarehouseId,
                WarehouseCode = wh.Code,
                WarehouseName = wh.Name,
                RegionName = reg != null ? reg.Name : "",
                batch.BatchCode,
                prod.Sku,
                ProductName = prod.Name,
                batch.ExpirationDate,
                line.Quantity,
                UnitPrice = unitPrice,
                doc.Reason,
                doc.CreatedByUserId
            }).ToArrayAsync(ct);

        if (whSet != null)
            disposalRows = disposalRows.Where(d => whSet.Contains(d.WarehouseId)).ToArray();

        var nearBatches = expiryBatches.Where(b => b.ExpiryStatus == "NearExpiry").ToArray();
        var expBatches = expiryBatches.Where(b => b.ExpiryStatus == "Expired").ToArray();
        var activeLocks = lockRows.Where(l => l.Status == InventoryLockStatuses.Active).ToArray();

        var summary = new InventoryRiskSummary(
            nearBatches.Length,
            nearBatches.Sum(b => b.PhysicalQuantity),
            expBatches.Length,
            expBatches.Sum(b => b.PhysicalQuantity),
            expiryBatches.Sum(b => b.EstimatedAtRiskValue),
            activeLocks.Length,
            activeLocks.Sum(l => l.LockedQuantity),
            activeLocks.Count(l => l.RequiresResolution),
            activeLocks.Sum(l => l.Value),
            disposalRows.Select(d => d.WriteOffId).Distinct().Count(),
            disposalRows.Sum(d => d.Quantity),
            disposalRows.Sum(d => d.Quantity * d.UnitPrice));

        BusinessPagedList<ExpiryActionBatchItem> expiryPage = EmptyPage<ExpiryActionBatchItem>(q.Page, q.PageSize);
        BusinessPagedList<InventoryLockReportItem> locksPage = EmptyPage<InventoryLockReportItem>(q.Page, q.PageSize);
        BusinessPagedList<DisposalReportItem> disposalsPage = EmptyPage<DisposalReportItem>(q.Page, q.PageSize);

        if (q.Tab == "expiry")
        {
            var filtered = expiryBatches
                .Where(b => string.IsNullOrWhiteSpace(q.Status) || b.ExpiryStatus == q.Status)
                .Where(b => string.IsNullOrWhiteSpace(searchTerm) ||
                    b.BatchCode.ToLowerInvariant().Contains(searchTerm) ||
                    b.Sku.ToLowerInvariant().Contains(searchTerm) ||
                    b.ProductName.ToLowerInvariant().Contains(searchTerm) ||
                    b.WarehouseCode.ToLowerInvariant().Contains(searchTerm))
                .OrderBy(b => b.ExpirationDate).ThenByDescending(b => b.EstimatedAtRiskValue)
                .ToArray();
            expiryPage = Paginate(filtered, q.Page, q.PageSize);
        }
        else if (q.Tab == "locks")
        {
            var filteredRaw = lockRows
                .Where(l => string.IsNullOrWhiteSpace(q.Status) || l.Status == q.Status)
                .Where(l => string.IsNullOrWhiteSpace(searchTerm) ||
                    l.BatchCode.ToLowerInvariant().Contains(searchTerm) ||
                    l.Sku.ToLowerInvariant().Contains(searchTerm) ||
                    l.WarehouseCode.ToLowerInvariant().Contains(searchTerm) ||
                    l.Reason.ToLowerInvariant().Contains(searchTerm))
                .OrderByDescending(l => l.Status == InventoryLockStatuses.Active)
                .ThenByDescending(l => l.CreatedAt)
                .ToArray();
            var lockUsers = await LoadUserNamesAsync(filteredRaw.SelectMany(l => new[] { l.LockedByUserId, l.ReleasedByUserId }), ct);
            var items = filteredRaw.Select(l =>
            {
                var refDate = l.ReleasedAt.HasValue ? ToVietnamDate(l.ReleasedAt.Value) : today;
                return new InventoryLockReportItem(
                    l.Id, l.WarehouseCode, l.WarehouseName, l.RegionName, l.BatchCode, l.Sku, l.ProductName,
                    l.ExpirationDate, l.LockedQuantity, l.Reason, l.RequiresResolution, l.Status,
                    Math.Max(0, refDate.DayNumber - ToVietnamDate(l.CreatedAt).DayNumber),
                    l.LockedByUserId.HasValue ? lockUsers.GetValueOrDefault(l.LockedByUserId.Value) : null,
                    l.ReleasedByUserId.HasValue ? lockUsers.GetValueOrDefault(l.ReleasedByUserId.Value) : null,
                    l.CreatedAt, l.ReleasedAt, l.Value, l.Note);
            }).ToArray();
            locksPage = Paginate(items, q.Page, q.PageSize);
        }
        else if (q.Tab == "disposals")
        {
            var filteredRaw = disposalRows
                .Where(d => string.IsNullOrWhiteSpace(searchTerm) ||
                    d.WriteOffCode.ToLowerInvariant().Contains(searchTerm) ||
                    d.BatchCode.ToLowerInvariant().Contains(searchTerm) ||
                    d.Sku.ToLowerInvariant().Contains(searchTerm) ||
                    d.WarehouseCode.ToLowerInvariant().Contains(searchTerm))
                .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.WriteOffCode)
                .ToArray();
            var creatorNames = await LoadUserNamesAsync(filteredRaw.Select(d => (Guid?)d.CreatedByUserId), ct);
            var items = filteredRaw.Select(d => new DisposalReportItem(
                d.Id, d.WriteOffId, d.WriteOffCode, d.CreatedAt, d.WarehouseId,
                d.WarehouseCode, d.WarehouseName, d.RegionName, d.BatchCode, d.Sku, d.ProductName,
                d.ExpirationDate, d.Quantity, d.UnitPrice, d.Quantity * d.UnitPrice,
                ExtractDisposalMethod(d.Reason),
                creatorNames.GetValueOrDefault(d.CreatedByUserId),
                null)).ToArray();
            disposalsPage = Paginate(items, q.Page, q.PageSize);
        }

        string[] notes =
        [
            "Hàng cận hạn/hết hạn và Khóa tồn kho (Active) là trạng thái hiện tại giúp Thủ kho chủ động bật giảm giá HSD hoặc lập biên bản hủy trước khi quá hạn.",
            "Biên bản tiêu hủy hiển thị các phiếu WriteOff đã lập trong kỳ lọc và định giá tổn thất theo đơn giá hiệu lực tại thời điểm hủy.",
        ];

        return new InventoryRiskReportResult(
            q.FromDate, q.ToDate, q.Tab, scope, summary, expiryPage, locksPage, disposalsPage, notes);
    }
}
