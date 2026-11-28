using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Refunds;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

public sealed partial class BusinessReportStore(AppDbContext db, TimeProvider timeProvider) : IBusinessReportStore
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    private static BusinessPagedList<T> EmptyPage<T>(int page, int pageSize) => new([], 0, page, pageSize);

    private static BusinessPagedList<T> Paginate<T>(IReadOnlyList<T> all, int page, int pageSize) =>
        new(all.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), all.Count, page, pageSize);

    private (DateTimeOffset Start, DateTimeOffset End) PeriodBounds(DateOnly fromDate, DateOnly toDate)
    {
        var start = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        var end = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        return (start, end);
    }

    private DateOnly TodayVietnam() =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(Vietnam).DateTime);

    private static DateOnly ToVietnamDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(timestamp.ToOffset(Vietnam).DateTime);

    private async Task<(HashSet<Guid>? GroupIds, string Scope)> ResolveGroupScopeAsync(
        Guid? requestedGroupId, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        if (actorRole is SystemRoles.Admin or SystemRoles.Operator)
            return requestedGroupId.HasValue ? ([requestedGroupId.Value], "Nhóm đã chọn") : (null, "Toàn hệ thống");

        if (actorRole == SystemRoles.SalesManager)
        {
            var managed = await ReportingScopeQueries.ManagedGroups(db, actorUserId)
                .Select(g => g.Id).Distinct().ToArrayAsync(ct);
            var set = managed.ToHashSet();
            if (requestedGroupId.HasValue)
            {
                if (!set.Contains(requestedGroupId.Value))
                    throw ReportOperationException.Forbidden("Nhóm bán hàng nằm ngoài phạm vi quản lý.");
                return ([requestedGroupId.Value], "Nhóm quản lý");
            }
            return (set, set.Count > 0 ? "Nhóm bán hàng được phân công" : "Chưa phân công nhóm");
        }

        throw ReportOperationException.Forbidden("Vai trò này không có quyền xem báo cáo kinh doanh.");
    }

    private async Task<(HashSet<Guid>? WarehouseIds, string Scope)> ResolveWarehouseScopeAsync(
        Guid? requestedWarehouseId, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        if (actorRole is SystemRoles.Admin or SystemRoles.Operator)
            return requestedWarehouseId.HasValue ? ([requestedWarehouseId.Value], "Kho đã chọn") : (null, "Toàn hệ thống");

        if (actorRole == SystemRoles.WarehouseKeeper)
        {
            var managed = await ReportingScopeQueries.ManagedWarehouses(db, actorUserId)
                .Where(w => w.IsActive)
                .Select(w => w.Id).Distinct().ToArrayAsync(ct);
            var set = managed.ToHashSet();
            if (requestedWarehouseId.HasValue)
            {
                if (!set.Contains(requestedWarehouseId.Value))
                    throw ReportOperationException.Forbidden("Kho được chọn nằm ngoài phạm vi phân công.");
                return ([requestedWarehouseId.Value], "Kho quản lý");
            }
            return (set, set.Count > 0 ? "Kho được phân công" : "Chưa phân công kho");
        }

        throw ReportOperationException.Forbidden("Vai trò này không có quyền xem báo cáo kho.");
    }

    private static string ExtractDisposalMethod(string? rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason)) return "Tiêu hủy tại kho";
        try
        {
            using var doc = JsonDocument.Parse(rawReason);
            if (doc.RootElement.TryGetProperty("Method", out var m) && m.GetString() is { Length: > 0 } str)
                return str;
        }
        catch { /* fallback */ }
        return "Tiêu hủy tại kho";
    }

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await db.UserAccountRecords.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    public async Task<FinancialReportResult> GetFinancialAsync(
        FinancialReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var (start, end) = PeriodBounds(q.FromDate, q.ToDate);
        var (groupSet, scope) = await ResolveGroupScopeAsync(q.SalesGroupId, actorUserId, actorRole, ct);
        var isGlobal = actorRole is SystemRoles.Admin or SystemRoles.Operator;

        var ordersQuery =
            from o in db.SalesOrderRecords.AsNoTracking()
            join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
            join g in db.SalesGroupRecords.AsNoTracking() on o.SalesGroupId equals g.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            join u in db.UserAccountRecords.AsNoTracking() on o.CreatedByUserId equals u.Id into uRows
            from u in uRows.DefaultIfEmpty()
            join p in db.PaymentRecords.AsNoTracking() on o.Id equals p.SalesOrderId into pRows
            from p in pRows.DefaultIfEmpty()
            where (!q.RegionId.HasValue || r.RegionId == q.RegionId.Value)
            select new
            {
                o.Id,
                o.OrderCode,
                o.CreatedAt,
                o.ServerReceivedAt,
                o.Status,
                o.PaymentMethod,
                o.RetailerId,
                RetailerCode = o.RetailerCodeSnapshot,
                RetailerName = o.RetailerNameSnapshot,
                RegionName = reg != null ? reg.Name : "",
                o.SalesGroupId,
                SalesGroupCode = g.Code,
                SalesGroupName = g.Name,
                SalesUserName = u != null ? u.FullName : "",
                o.SubtotalAmount,
                o.DiscountAmount,
                o.FinalAmount,
                AdjustedAmount = p != null ? p.AdjustedAmount : (decimal?)null,
                ConfirmedAmount = p != null ? p.ConfirmedAmount : (decimal?)null,
                DeliveredDate = p != null ? p.DeliveredDate : null,
                PaymentId = p != null ? p.Id : (Guid?)null,
                PaymentStatus = p != null ? p.Status : null,
                PaymentConfirmedAt = p != null ? p.ConfirmedAt : null
            };

        var allOrders = await ordersQuery.ToArrayAsync(ct);
        if (groupSet != null)
            allOrders = allOrders.Where(o => groupSet.Contains(o.SalesGroupId)).ToArray();

        var completedIds = allOrders.Where(o => o.Status == OrderStatuses.Completed).Select(o => o.Id).ToArray();
        var completionMap = completedIds.Length == 0
            ? new Dictionary<Guid, DateOnly>()
            : (await db.OrderStatusHistoryRecords.AsNoTracking()
                    .Where(h => h.ToStatus == OrderStatuses.Completed && completedIds.Contains(h.SalesOrderId))
                    .Select(h => new { h.SalesOrderId, h.ChangedAt })
                    .ToArrayAsync(ct))
                .GroupBy(h => h.SalesOrderId)
                .ToDictionary(g => g.Key, g => ToVietnamDate(g.Max(x => x.ChangedAt)));

        DateOnly? RecognitionDateOf(Guid orderId, string status, DateOnly? deliveredDate, DateTimeOffset receivedAt)
        {
            if (status != OrderStatuses.Completed) return null;
            if (completionMap.TryGetValue(orderId, out var d)) return d;
            return deliveredDate ?? ToVietnamDate(receivedAt);
        }

        var createdInPeriod = allOrders.Where(o => o.CreatedAt >= start && o.CreatedAt < end).ToArray();
        var recognizedInPeriod = allOrders
            .Select(o => new { Row = o, RecDate = RecognitionDateOf(o.Id, o.Status, o.DeliveredDate, o.ServerReceivedAt) })
            .Where(x => x.RecDate.HasValue && x.RecDate.Value >= q.FromDate && x.RecDate.Value <= q.ToDate)
            .ToArray();

        var grossOrderValue = recognizedInPeriod.Sum(x => x.Row.SubtotalAmount);
        var discountAmount = recognizedInPeriod.Sum(x => x.Row.DiscountAmount);
        var recognizedRevenue = recognizedInPeriod.Sum(x => x.Row.AdjustedAmount ?? x.Row.FinalAmount);
        var claimAdjustmentAmount = recognizedInPeriod.Sum(x =>
            Math.Max(0m, x.Row.FinalAmount - (x.Row.AdjustedAmount ?? x.Row.FinalAmount)));

        // Collections via confirmed PaymentReceipts + legacy direct confirmed payments
        var rawReceipts = await (
            from pr in db.PaymentReceiptRecords.AsNoTracking()
            where pr.Status == PaymentReceiptStatus.Confirmed && pr.ConfirmedAt >= start && pr.ConfirmedAt < end
            join r in db.RetailerRecords.AsNoTracking() on pr.RetailerId equals r.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            where (!q.RegionId.HasValue || r.RegionId == q.RegionId.Value)
            select new
            {
                pr.Id,
                pr.ReceiptCode,
                pr.ConfirmedAt,
                pr.RetailerId,
                RetailerCode = r.Code,
                RetailerName = r.Name,
                RegionName = reg != null ? reg.Name : "",
                pr.ReceivedAmount,
                pr.UnappliedCreditAmount,
                pr.ConfirmedByUserId,
                pr.Reference,
                pr.Note
            }).ToArrayAsync(ct);

        var receiptIds = rawReceipts.Select(r => r.Id).ToArray();
        var payOsReceiptIds = receiptIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.RetailerDebtPayOsCheckoutRecords.AsNoTracking()
                    .Where(c => c.PaymentReceiptId.HasValue && receiptIds.Contains(c.PaymentReceiptId.Value))
                    .Select(c => c.PaymentReceiptId!.Value)
                    .ToArrayAsync(ct))
                .ToHashSet();

        var receiptAllocations = receiptIds.Length == 0
            ? []
            : await (
                from a in db.PaymentReceiptAllocationRecords.AsNoTracking()
                where receiptIds.Contains(a.PaymentReceiptId)
                join p in db.PaymentRecords.AsNoTracking() on a.PaymentId equals p.Id
                join o in db.SalesOrderRecords.AsNoTracking() on p.SalesOrderId equals o.Id
                select new { a.PaymentReceiptId, a.Amount, o.SalesGroupId }
            ).ToArrayAsync(ct);

        var scopedOrderIds = allOrders.Select(o => o.Id).ToHashSet();
        var scopedPaymentIds = allOrders.Where(o => o.PaymentId.HasValue).Select(o => o.PaymentId!.Value).ToHashSet();

        decimal collectedAmount;
        decimal unappliedCredit;
        List<FinancialReceiptItem> receiptItems = [];
        var userNamesForReceipts = await LoadUserNamesAsync(rawReceipts.Select(r => r.ConfirmedByUserId), ct);

        if (isGlobal && !q.SalesGroupId.HasValue)
        {
            collectedAmount = rawReceipts.Sum(r => r.ReceivedAmount);
            unappliedCredit = rawReceipts.Sum(r => r.UnappliedCreditAmount);
            receiptItems = rawReceipts.Select(r =>
            {
                var allocated = receiptAllocations.Where(a => a.PaymentReceiptId == r.Id).Sum(a => a.Amount);
                var isPayOs = payOsReceiptIds.Contains(r.Id)
                    || (!string.IsNullOrWhiteSpace(r.Reference) && r.Reference.Contains("PAYOS", StringComparison.OrdinalIgnoreCase));
                return new FinancialReceiptItem(
                    r.Id, r.ReceiptCode, r.ConfirmedAt, isPayOs ? "PayOS" : "Chuyển khoản / Thu nợ",
                    r.RetailerId, r.RetailerCode, r.RetailerName, r.RegionName,
                    r.ReceivedAmount, allocated, r.UnappliedCreditAmount,
                    r.ConfirmedByUserId.HasValue ? userNamesForReceipts.GetValueOrDefault(r.ConfirmedByUserId.Value) : null,
                    r.Reference, r.Note);
            }).ToList();
        }
        else
        {
            var allocByReceipt = receiptAllocations
                .Where(a => groupSet == null || groupSet.Contains(a.SalesGroupId))
                .GroupBy(a => a.PaymentReceiptId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
            collectedAmount = allocByReceipt.Values.Sum();
            unappliedCredit = 0m;
            receiptItems = rawReceipts
                .Where(r => allocByReceipt.ContainsKey(r.Id))
                .Select(r =>
                {
                    var allocated = allocByReceipt[r.Id];
                    var isPayOs = payOsReceiptIds.Contains(r.Id)
                        || (!string.IsNullOrWhiteSpace(r.Reference) && r.Reference.Contains("PAYOS", StringComparison.OrdinalIgnoreCase));
                    return new FinancialReceiptItem(
                        r.Id, r.ReceiptCode, r.ConfirmedAt, isPayOs ? "PayOS" : "Chuyển khoản / Thu nợ",
                        r.RetailerId, r.RetailerCode, r.RetailerName, r.RegionName,
                        allocated, allocated, 0m,
                        r.ConfirmedByUserId.HasValue ? userNamesForReceipts.GetValueOrDefault(r.ConfirmedByUserId.Value) : null,
                        r.Reference, r.Note);
                }).ToList();
        }

        // Legacy payments settled directly without a PaymentReceiptAllocation record
        var legacyPaymentIdsWithReceipt = (await db.PaymentReceiptAllocationRecords.AsNoTracking()
            .Where(a => scopedPaymentIds.Contains(a.PaymentId) &&
                        db.PaymentReceiptRecords.Any(pr => pr.Id == a.PaymentReceiptId && pr.Status == PaymentReceiptStatus.Confirmed))
            .Select(a => a.PaymentId)
            .Distinct()
            .ToArrayAsync(ct)).ToHashSet();

        var legacyCollected = allOrders
            .Where(o => o.PaymentId.HasValue
                && o.PaymentStatus != PaymentStatuses.Cancelled
                && (o.ConfirmedAmount ?? 0m) > 0
                && o.PaymentConfirmedAt >= start && o.PaymentConfirmedAt < end
                && !legacyPaymentIdsWithReceipt.Contains(o.PaymentId.Value))
            .ToArray();
        collectedAmount += legacyCollected.Sum(o => o.ConfirmedAmount ?? 0m);
        foreach (var l in legacyCollected)
        {
            receiptItems.Add(new FinancialReceiptItem(
                l.PaymentId!.Value, l.OrderCode, l.PaymentConfirmedAt,
                l.PaymentMethod == OrderPaymentMethods.Prepaid ? "Prepaid / PayOS trực tiếp" : "Thanh toán đơn trực tiếp",
                l.RetailerId, l.RetailerCode, l.RetailerName, l.RegionName,
                l.ConfirmedAmount ?? 0m, l.ConfirmedAmount ?? 0m, 0m,
                null, l.OrderCode, "Ghi nhận từ thanh toán đơn"));
        }

        // Manual Refunds (Completed in period + open snapshot)
        var rawRefunds = await (
            from mr in db.ManualRefundRecords.AsNoTracking()
            join o in db.SalesOrderRecords.AsNoTracking() on mr.SalesOrderId equals o.Id
            join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            where !q.RegionId.HasValue || r.RegionId == q.RegionId.Value
            select new
            {
                mr.Id,
                mr.SalesOrderId,
                o.OrderCode,
                o.SalesGroupId,
                o.RetailerId,
                RetailerCode = o.RetailerCodeSnapshot,
                RetailerName = o.RetailerNameSnapshot,
                RegionName = reg != null ? reg.Name : "",
                mr.Amount,
                mr.Status,
                mr.Recipient,
                mr.Reason,
                mr.CreatedAt,
                mr.SubmittedByUserId,
                mr.SubmittedAt,
                mr.VerifiedByUserId,
                mr.VerifiedAt,
                mr.TransactionReference
            }).ToArrayAsync(ct);

        if (groupSet != null)
            rawRefunds = rawRefunds.Where(r => groupSet.Contains(r.SalesGroupId)).ToArray();

        var refundedInPeriod = rawRefunds.Where(r =>
            r.Status == RefundStatuses.Refunded && (r.VerifiedAt ?? r.CreatedAt) >= start && (r.VerifiedAt ?? r.CreatedAt) < end).ToArray();
        var pendingRefunds = rawRefunds.Where(r => RefundStatuses.Open.Contains(r.Status)).ToArray();
        var refundedAmount = refundedInPeriod.Sum(r => r.Amount);
        var pendingRefundAmount = pendingRefunds.Sum(r => r.Amount);

        decimal writeOffLoss = 0m;
        if (isGlobal && !q.SalesGroupId.HasValue)
        {
            var writeOffLines = await (
                from line in db.WriteOffItemRecords.AsNoTracking()
                join doc in db.WriteOffRecords.AsNoTracking() on line.WriteOffId equals doc.Id
                join wh in db.WarehouseRecords.AsNoTracking() on doc.WarehouseId equals wh.Id
                join batch in db.BatchRecords.AsNoTracking() on line.BatchId equals batch.Id
                where doc.CreatedAt >= start && doc.CreatedAt < end
                    && (!q.RegionId.HasValue || wh.RegionId == q.RegionId.Value)
                let unitPrice = db.ProductPriceRecords
                    .Where(pr => pr.ProductId == batch.ProductId && pr.EffectiveFrom <= doc.CreatedAt && (pr.EffectiveTo == null || pr.EffectiveTo > doc.CreatedAt))
                    .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault()
                    ?? db.ProductPriceRecords.Where(pr => pr.ProductId == batch.ProductId && pr.IsActive)
                        .OrderByDescending(pr => pr.EffectiveFrom).Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault()
                    ?? 0m
                select line.Quantity * unitPrice
            ).ToArrayAsync(ct);
            writeOffLoss = writeOffLines.Sum();
        }

        var summary = new FinancialSummary(
            createdInPeriod.Length,
            recognizedInPeriod.Length,
            grossOrderValue,
            discountAmount,
            claimAdjustmentAmount,
            recognizedRevenue,
            collectedAmount,
            unappliedCredit,
            refundedAmount,
            pendingRefundAmount,
            writeOffLoss,
            isGlobal && !q.SalesGroupId.HasValue,
            collectedAmount - refundedAmount);

        var term = q.Search?.Trim().ToLowerInvariant();

        BusinessPagedList<FinancialOrderItem> ordersPage = EmptyPage<FinancialOrderItem>(q.Page, q.PageSize);
        BusinessPagedList<FinancialReceiptItem> receiptsPage = EmptyPage<FinancialReceiptItem>(q.Page, q.PageSize);
        BusinessPagedList<FinancialRefundItem> refundsPage = EmptyPage<FinancialRefundItem>(q.Page, q.PageSize);

        if (q.Tab == "orders")
        {
            var filtered = recognizedInPeriod.Select(x => x.Row)
                .Concat(createdInPeriod.Where(o => o.Status != OrderStatuses.Completed))
                .DistinctBy(o => o.Id)
                .Where(o => string.IsNullOrWhiteSpace(q.Status) || o.Status == q.Status)
                .Where(o => string.IsNullOrWhiteSpace(q.PaymentMethod) || o.PaymentMethod == q.PaymentMethod)
                .Where(o => string.IsNullOrWhiteSpace(term) ||
                    o.OrderCode.ToLowerInvariant().Contains(term) ||
                    o.RetailerCode.ToLowerInvariant().Contains(term) ||
                    o.RetailerName.ToLowerInvariant().Contains(term) ||
                    o.SalesGroupCode.ToLowerInvariant().Contains(term) ||
                    o.SalesUserName.ToLowerInvariant().Contains(term))
                .Select(o =>
                {
                    var recDate = RecognitionDateOf(o.Id, o.Status, o.DeliveredDate, o.ServerReceivedAt);
                    var net = o.AdjustedAmount ?? o.FinalAmount;
                    var adj = Math.Max(0m, o.FinalAmount - net);
                    var paid = o.ConfirmedAmount ?? 0m;
                    return new FinancialOrderItem(
                        o.Id, o.OrderCode, o.CreatedAt, recDate,
                        o.RetailerId, o.RetailerCode, o.RetailerName, o.RegionName,
                        o.SalesGroupCode, o.SalesGroupName, o.SalesUserName, o.Status, o.PaymentMethod,
                        o.SubtotalAmount, o.DiscountAmount, adj, net, paid, Math.Max(0m, net - paid));
                })
                .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.OrderCode)
                .ToArray();
            ordersPage = Paginate(filtered, q.Page, q.PageSize);
        }
        else if (q.Tab == "receipts")
        {
            var filtered = receiptItems
                .Where(r => string.IsNullOrWhiteSpace(term) ||
                    r.ReceiptCode.ToLowerInvariant().Contains(term) ||
                    r.RetailerCode.ToLowerInvariant().Contains(term) ||
                    r.RetailerName.ToLowerInvariant().Contains(term) ||
                    (r.Reference ?? "").ToLowerInvariant().Contains(term))
                .OrderByDescending(r => r.ConfirmedAt).ThenBy(r => r.ReceiptCode)
                .ToArray();
            receiptsPage = Paginate(filtered, q.Page, q.PageSize);
        }
        else if (q.Tab == "refunds")
        {
            var refundUsers = await LoadUserNamesAsync(
                rawRefunds.SelectMany(r => new[] { r.SubmittedByUserId, r.VerifiedByUserId }), ct);
            var filtered = rawRefunds
                .Where(r => (r.CreatedAt >= start && r.CreatedAt < end)
                    || (r.VerifiedAt.HasValue && r.VerifiedAt.Value >= start && r.VerifiedAt.Value < end))
                .Where(r => string.IsNullOrWhiteSpace(q.Status) || r.Status == q.Status)
                .Where(r => string.IsNullOrWhiteSpace(term) ||
                    r.OrderCode.ToLowerInvariant().Contains(term) ||
                    r.RetailerCode.ToLowerInvariant().Contains(term) ||
                    r.RetailerName.ToLowerInvariant().Contains(term))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new FinancialRefundItem(
                    r.Id, $"HT-{r.Id.ToString("N")[..8].ToUpperInvariant()}", r.SalesOrderId, r.OrderCode,
                    r.RetailerId, r.RetailerCode, r.RetailerName, r.RegionName, r.Amount, r.Status,
                    r.Recipient, r.Reason, r.CreatedAt,
                    r.SubmittedByUserId.HasValue ? refundUsers.GetValueOrDefault(r.SubmittedByUserId.Value) : null,
                    r.SubmittedAt,
                    r.VerifiedByUserId.HasValue ? refundUsers.GetValueOrDefault(r.VerifiedByUserId.Value) : null,
                    r.VerifiedAt, r.TransactionReference))
                .ToArray();
            refundsPage = Paginate(filtered, q.Page, q.PageSize);
        }

        List<string> notes =
        [
            "Tổng hợp 100% phía máy chủ: Doanh thu ghi nhận tính trên đơn Hoàn tất trong kỳ theo giá trị sau khuyến mãi và điều chỉnh khiếu nại.",
            "Tiền thực thu tổng hợp từ biên nhận PaymentReceipt đã xác nhận (PayOS / chuyển khoản ngân hàng do kế toán xác nhận; Sales không thu tiền mặt).",
        ];
        if (isGlobal && !q.SalesGroupId.HasValue)
            notes.Add("Tổn thất hủy hàng chỉ hiển thị ở cấp toàn hệ thống/khu vực (khi không lọc theo nhóm Sales).");

        return new FinancialReportResult(
            q.FromDate, q.ToDate, q.Tab, scope, summary, ordersPage, receiptsPage, refundsPage, notes);
    }

    public async Task<ReceivablesReportResult> GetReceivablesAsync(
        ReceivablesReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var today = TodayVietnam();
        var (groupSet, scope) = await ResolveGroupScopeAsync(q.SalesGroupId, actorUserId, actorRole, ct);

        var rows = await (
            from p in db.PaymentRecords.AsNoTracking()
            where p.Method == OrderPaymentMethods.Credit && p.Status != PaymentStatuses.Cancelled && p.DeliveredDate != null
            join o in db.SalesOrderRecords.AsNoTracking() on p.SalesOrderId equals o.Id
            join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
            join g in db.SalesGroupRecords.AsNoTracking() on o.SalesGroupId equals g.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            join u in db.UserAccountRecords.AsNoTracking() on o.CreatedByUserId equals u.Id into uRows
            from u in uRows.DefaultIfEmpty()
            where !q.RegionId.HasValue || r.RegionId == q.RegionId.Value
            select new
            {
                p.Id,
                p.SalesOrderId,
                o.OrderCode,
                o.SalesGroupId,
                o.RetailerId,
                RetailerCode = o.RetailerCodeSnapshot,
                RetailerName = o.RetailerNameSnapshot,
                RegionName = reg != null ? reg.Name : "",
                SalesGroupCode = g.Code,
                SalesGroupName = g.Name,
                SalesUserName = u != null ? u.FullName : "",
                o.CreatedAt,
                p.DeliveredDate,
                p.DueDate,
                p.AdjustedAmount,
                p.ConfirmedAmount,
                p.Status,
                p.PayOsLinkStatus,
                r.CreditLimit,
                r.CreditTermDays,
                p.Note
            }).ToArrayAsync(ct);

        if (groupSet != null)
            rows = rows.Where(x => groupSet.Contains(x.SalesGroupId)).ToArray();

        if (q.DueFrom.HasValue)
            rows = rows.Where(x => x.DueDate.HasValue && x.DueDate.Value >= q.DueFrom.Value).ToArray();
        if (q.DueTo.HasValue)
            rows = rows.Where(x => x.DueDate.HasValue && x.DueDate.Value <= q.DueTo.Value).ToArray();

        var term = q.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(term))
        {
            rows = rows.Where(x =>
                x.OrderCode.ToLowerInvariant().Contains(term) ||
                x.RetailerCode.ToLowerInvariant().Contains(term) ||
                x.RetailerName.ToLowerInvariant().Contains(term) ||
                x.SalesGroupCode.ToLowerInvariant().Contains(term) ||
                x.SalesUserName.ToLowerInvariant().Contains(term)).ToArray();
        }

        string AgingBucketOf(DateOnly? due, decimal outstanding)
        {
            if (outstanding <= 0) return "Settled";
            if (!due.HasValue || due.Value >= today) return "Current";
            var days = today.DayNumber - due.Value.DayNumber;
            return days <= 7 ? "Overdue_1_7" : days <= 30 ? "Overdue_8_30" : "Overdue_31_Plus";
        }

        var openRows = rows.Where(x => x.AdjustedAmount > x.ConfirmedAmount).ToArray();
        var settledCount = rows.Length - openRows.Length;

        decimal BucketSum(string bucket) => openRows
            .Where(x => AgingBucketOf(x.DueDate, x.AdjustedAmount - x.ConfirmedAmount) == bucket)
            .Sum(x => x.AdjustedAmount - x.ConfirmedAmount);

        var currentAmt = BucketSum("Current");
        var d1To7Amt = BucketSum("Overdue_1_7");
        var d8To30Amt = BucketSum("Overdue_8_30");
        var d31PlusAmt = BucketSum("Overdue_31_Plus");
        var totalOpen = openRows.Sum(x => x.AdjustedAmount - x.ConfirmedAmount);

        var summary = new ReceivablesSummary(
            openRows.Length,
            settledCount,
            openRows.Select(x => x.RetailerId).Distinct().Count(),
            totalOpen,
            currentAmt,
            d1To7Amt,
            d8To30Amt,
            d31PlusAmt,
            d1To7Amt + d8To30Amt + d31PlusAmt);

        var buckets = new ReceivablesAgingBucket[]
        {
            new("Current", "Chưa đến hạn", openRows.Count(x => AgingBucketOf(x.DueDate, x.AdjustedAmount - x.ConfirmedAmount) == "Current"), currentAmt),
            new("Overdue_1_7", "Quá hạn 1–7 ngày", openRows.Count(x => AgingBucketOf(x.DueDate, x.AdjustedAmount - x.ConfirmedAmount) == "Overdue_1_7"), d1To7Amt),
            new("Overdue_8_30", "Quá hạn 8–30 ngày", openRows.Count(x => AgingBucketOf(x.DueDate, x.AdjustedAmount - x.ConfirmedAmount) == "Overdue_8_30"), d8To30Amt),
            new("Overdue_31_Plus", "Quá hạn > 30 ngày", openRows.Count(x => AgingBucketOf(x.DueDate, x.AdjustedAmount - x.ConfirmedAmount) == "Overdue_31_Plus"), d31PlusAmt),
        };

        var working = q.IncludeSettled ? rows : openRows;
        var mapped = working
            .Select(x =>
            {
                var outstanding = Math.Max(0m, x.AdjustedAmount - x.ConfirmedAmount);
                var bucket = AgingBucketOf(x.DueDate, outstanding);
                var daysOverdue = outstanding > 0 && x.DueDate.HasValue && x.DueDate.Value < today
                    ? today.DayNumber - x.DueDate.Value.DayNumber : 0;
                return new ReceivableInvoiceItem(
                    x.Id, x.SalesOrderId, x.OrderCode, x.RetailerId, x.RetailerCode, x.RetailerName,
                    x.RegionName, x.SalesGroupCode, x.SalesGroupName, x.SalesUserName,
                    x.CreatedAt, x.DeliveredDate, x.DueDate, daysOverdue, bucket,
                    x.AdjustedAmount, x.ConfirmedAmount, outstanding, x.Status, x.PayOsLinkStatus, x.CreditLimit, x.Note);
            })
            .Where(x => string.IsNullOrWhiteSpace(q.Bucket) || x.AgingBucket == q.Bucket)
            .OrderByDescending(x => x.DaysOverdue)
            .ThenByDescending(x => x.OutstandingAmount)
            .ThenBy(x => x.DueDate ?? DateOnly.MaxValue)
            .ToArray();

        BusinessPagedList<ReceivableInvoiceItem> invoicesPage = EmptyPage<ReceivableInvoiceItem>(q.Page, q.PageSize);
        BusinessPagedList<ReceivableCustomerItem> customersPage = EmptyPage<ReceivableCustomerItem>(q.Page, q.PageSize);

        if (q.View == "customers")
        {
            var retailerIds = mapped.Select(x => x.RetailerId).Distinct().ToArray();
            var unappliedByRetailer = retailerIds.Length == 0
                ? new Dictionary<Guid, decimal>()
                : await db.PaymentReceiptRecords.AsNoTracking()
                    .Where(pr => pr.Status == PaymentReceiptStatus.Confirmed && retailerIds.Contains(pr.RetailerId) && pr.UnappliedCreditAmount > 0)
                    .GroupBy(pr => pr.RetailerId)
                    .Select(g => new { RetailerId = g.Key, Total = g.Sum(x => x.UnappliedCreditAmount) })
                    .ToDictionaryAsync(x => x.RetailerId, x => x.Total, ct);
            var termsByRetailer = working.GroupBy(x => x.RetailerId)
                .ToDictionary(g => g.Key, g => g.First().CreditTermDays);

            var customers = mapped
                .GroupBy(x => new { x.RetailerId, x.RetailerCode, x.RetailerName, x.RegionName, x.SalesGroupName, x.SalesUserName, x.CreditLimit })
                .Select(g => new ReceivableCustomerItem(
                    g.Key.RetailerId, g.Key.RetailerCode, g.Key.RetailerName, g.Key.RegionName,
                    g.Key.SalesGroupName, g.Key.SalesUserName,
                    g.Count(x => x.OutstandingAmount > 0),
                    g.Count(x => x.DaysOverdue > 0),
                    g.Sum(x => x.AdjustedAmount),
                    g.Sum(x => x.ConfirmedAmount),
                    g.Sum(x => x.OutstandingAmount),
                    g.Where(x => x.DaysOverdue > 0).Sum(x => x.OutstandingAmount),
                    g.Max(x => x.DaysOverdue),
                    g.Key.CreditLimit,
                    termsByRetailer.GetValueOrDefault(g.Key.RetailerId, 0),
                    unappliedByRetailer.GetValueOrDefault(g.Key.RetailerId, 0m)))
                .OrderByDescending(c => c.OverdueTotal)
                .ThenByDescending(c => c.OutstandingTotal)
                .ThenBy(c => c.RetailerName)
                .ToArray();
            customersPage = Paginate(customers, q.Page, q.PageSize);
        }
        else
        {
            invoicesPage = Paginate(mapped, q.Page, q.PageSize);
        }

        string[] notes =
        [
            "Mặc định hiển thị toàn bộ công nợ Credit đang còn phải thu tính đến hôm nay (không bị cắt mất đơn các tháng trước theo ngày đặt đơn).",
            "Khi có nhu cầu rà soát hạn thanh toán theo khoảng thời gian, dùng bộ lọc Từ hạn thu / Đến hạn thu.",
        ];

        return new ReceivablesReportResult(today, q.View, scope, summary, buckets, invoicesPage, customersPage, notes);
    }
}
