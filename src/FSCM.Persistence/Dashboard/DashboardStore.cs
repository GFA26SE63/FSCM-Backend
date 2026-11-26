using FSCM.Application.Features.Dashboard;
using FSCM.Application.Features.Reports;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Dashboard;

public sealed class DashboardStore(AppDbContext db) : IDashboardStore
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private sealed record CompletedRow(Guid Id, Guid CreatorId, Guid GroupId, Guid RetailerId,
        string CreatorRole, decimal Amount, DateOnly Date);
    private sealed record CollectionRow(DateTimeOffset At, decimal Amount);
    /// <summary>Settable members so EF can keep composing (joins, sums) on top of the projection.</summary>
    private sealed class RecognizedRow
    {
        public Guid Id { get; set; }
        public Guid CreatedByUserId { get; set; }
        public Guid SalesGroupId { get; set; }
        public Guid RetailerId { get; set; }
        public string CreatorRole { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTimeOffset? At { get; set; }
        public DateOnly? Delivered { get; set; }
        public DateTimeOffset ServerReceivedAt { get; set; }
    }
    private static readonly string[] AllocationFailureStatuses =
        [OrderStatuses.AllocationFailed, OrderStatuses.OutOfStock, OrderStatuses.AllocationConflict];
    private static readonly string[] QuoteAcceptedStatuses =
        [OrderStatuses.PaymentPending, OrderStatuses.ConvertedToPo, OrderStatuses.Picking, OrderStatuses.Issued,
         OrderStatuses.Delivering, OrderStatuses.Delivered, OrderStatuses.Completed];
    private static readonly string[] QuoteDeclinedStatuses = [OrderStatuses.QuoteRejected, OrderStatuses.QuoteExpired];

    /// <summary>
    /// Completed orders recognised in [start, end): by completion time, else delivered date, else
    /// server-received time (older orders without completion history).
    /// </summary>
    private IQueryable<RecognizedRow> Recognized(IQueryable<SalesOrder> orders,
        DateTimeOffset start, DateTimeOffset end, DateOnly periodFrom, DateOnly periodTo)
    {
        var completionDates = db.OrderStatusHistoryRecords.AsNoTracking().Where(h => h.ToStatus == OrderStatuses.Completed)
            .GroupBy(h => h.SalesOrderId).Select(g => new { Id = g.Key, At = (DateTimeOffset?)g.Max(h => h.ChangedAt) });
        return from order in orders.Where(o => o.Status == OrderStatuses.Completed)
            join payment in db.PaymentRecords.AsNoTracking() on order.Id equals payment.SalesOrderId into paymentRows
            from payment in paymentRows.DefaultIfEmpty()
            join date in completionDates on order.Id equals date.Id into dateRows
            from date in dateRows.DefaultIfEmpty()
            join creator in db.UserAccountRecords.AsNoTracking() on order.CreatedByUserId equals creator.Id
            where date.At != null ? date.At >= start && date.At < end :
                payment != null && payment.DeliveredDate != null ? payment.DeliveredDate >= periodFrom && payment.DeliveredDate <= periodTo :
                order.ServerReceivedAt >= start && order.ServerReceivedAt < end
            select new RecognizedRow
            {
                Id = order.Id, CreatedByUserId = order.CreatedByUserId, SalesGroupId = order.SalesGroupId,
                RetailerId = order.RetailerId, CreatorRole = creator.Role,
                Amount = payment != null ? payment.AdjustedAmount : order.FinalAmount,
                At = date.At, Delivered = payment == null ? (DateOnly?)null : payment.DeliveredDate,
                ServerReceivedAt = order.ServerReceivedAt
            };
    }

    /// <summary>Batches returned for the Portal's paged "Batch cần chú ý" list (shown 10 at a time).</summary>
    private const int ExpiryRiskLimit = 200;
    /// <summary>Work items per source and in total; the Portal shows them 10 at a time.</summary>
    private const int WorkItemLimit = 200;
    /// <summary>Best-selling SKUs returned for "Top SKU bán chạy" (shown 10 at a time).</summary>
    private const int TopProductLimit = 100;

    private static decimal Percent(decimal part, decimal whole) =>
        whole <= 0 ? 0m : Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);

    public async Task<DashboardResult> ReadAsync(Guid userId, string role, DashboardQuery query, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.UserAccountRecords.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null || user.Role != role) throw ReportOperationException.Forbidden("Tài khoản hoặc vai trò đã thay đổi. Vui lòng đăng nhập lại.");
        var global = role is SystemRoles.Admin or SystemRoles.Operator;
        var keeper = role == SystemRoles.WarehouseKeeper;
        var manager = role == SystemRoles.SalesManager;
        var periodFrom = query.FromDate!.Value; var periodTo = query.ToDate!.Value;
        var start = new DateTimeOffset(periodFrom.ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        var end = new DateTimeOffset(periodTo.AddDays(1).ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        var today = DateOnly.FromDateTime(now.ToOffset(Vietnam).DateTime);

        var groupQuery = global ? db.SalesGroupRecords.AsNoTracking() : ReportingScopeQueries.ManagedGroups(db, userId);
        var warehouseQuery = global ? db.WarehouseRecords.AsNoTracking() : ReportingScopeQueries.ManagedWarehouses(db, userId);
        var groups = keeper ? [] : await groupQuery.Select(g => new DashboardOption(g.Id, g.Code, g.Name, g.RegionId)).ToArrayAsync(ct);
        var warehouses = manager ? [] : await warehouseQuery.Select(w => new DashboardOption(w.Id, w.Code, w.Name, w.RegionId)).ToArrayAsync(ct);
        var regionIds = groups.Select(g => g.RegionId!.Value).Concat(warehouses.Select(w => w.RegionId!.Value)).Distinct().ToArray();
        var regions = await db.RegionRecords.AsNoTracking().Where(r => global || regionIds.Contains(r.Id))
            .Select(r => new DashboardOption(r.Id, r.Code, r.Name, null)).ToArrayAsync(ct);
        if (query.RegionId.HasValue && !regions.Any(r => r.Id == query.RegionId) ||
            query.SalesGroupId.HasValue && !groups.Any(g => g.Id == query.SalesGroupId) ||
            query.WarehouseId.HasValue && !warehouses.Any(w => w.Id == query.WarehouseId))
            throw ReportOperationException.Forbidden("Bộ lọc nằm ngoài phạm vi báo cáo được phân công.");
        if (query.RegionId.HasValue &&
            (query.SalesGroupId.HasValue && groups.Single(g => g.Id == query.SalesGroupId).RegionId != query.RegionId ||
             query.WarehouseId.HasValue && warehouses.Single(w => w.Id == query.WarehouseId).RegionId != query.RegionId))
            throw ReportOperationException.Validation("Kho/nhóm Sales không thuộc khu vực đang chọn.");
        var allowedGroups = groups.Where(g => (!query.RegionId.HasValue || g.RegionId == query.RegionId) &&
            (!query.SalesGroupId.HasValue || g.Id == query.SalesGroupId)).Select(g => g.Id).ToArray();
        var allowedWarehouses = warehouses.Where(w => (!query.RegionId.HasValue || w.RegionId == query.RegionId) &&
            (!query.WarehouseId.HasValue || w.Id == query.WarehouseId)).Select(w => w.Id).ToArray();
        var hasAssignments = global || (keeper ? warehouses.Length > 0 : groups.Length > 0);
        var scope = new DashboardScope(global, hasAssignments, global ? "Toàn hệ thống" : keeper ? "Khu vực / kho được phân công" : "Khu vực / nhóm Sales được quản lý",
            regions, warehouses, groups);
        var notes = new List<string> { "Đơn và doanh thu theo kỳ đã chọn; tồn kho, công nợ và việc chờ là số hiện tại." };
        if (!keeper) notes.Add("Doanh thu ghi nhận theo đơn Completed và thời điểm hoàn tất; giá trị đã trừ khuyến mãi và điều chỉnh.");
        if (!manager) notes.Add("Bộ lọc kho áp dụng cho tồn kho, nhập, soạn và shipment. Báo cáo tiền/đơn theo khu vực và nhóm Sales.");
        var cards = new List<DashboardCard>(); var work = new List<DashboardWorkItem>();
        var statuses = Array.Empty<DashboardStatus>(); var trend = Array.Empty<DashboardDay>();
        var products = Array.Empty<DashboardProduct>(); var team = Array.Empty<DashboardSalesPerson>();
        var inventory = Array.Empty<DashboardWarehouse>(); var risks = Array.Empty<DashboardExpiry>();
        var customers = Array.Empty<DashboardCustomer>(); DashboardHealth? health = null;
        var debtAging = Array.Empty<DashboardAgingBucket>(); var stockCover = Array.Empty<DashboardStockCover>();
        var expiryRiskTotal = 0;
        decimal? unattributed = null;
        var orders = db.SalesOrderRecords.AsNoTracking().Where(o => !keeper && allowedGroups.Contains(o.SalesGroupId));
        var periodOrders = orders.Where(o => o.ServerReceivedAt >= start && o.ServerReceivedAt < end);
        var orderIds = orders.Select(o => o.Id);
        var payments = db.PaymentRecords.AsNoTracking().Where(p => orderIds.Contains(p.SalesOrderId) && p.Status != "Cancelled");
        var completed = Array.Empty<CompletedRow>(); var collections = Array.Empty<CollectionRow>();

        if (global)
        {
            var retailers = db.RetailerRecords.AsNoTracking().Where(r =>
                (!query.RegionId.HasValue || r.RegionId == query.RegionId) &&
                (!query.SalesGroupId.HasValue || db.RetailerSalesAssignmentRecords.Any(a =>
                    a.RetailerId == r.Id && a.SalesGroupId == query.SalesGroupId && a.IsActive && a.UnassignedAt == null)));
            var redemptionQuery = db.RewardRedemptionRecords.AsNoTracking()
                .Where(x => retailers.Select(r => r.Id).Contains(x.RetailerId));
            var redemptionScope = (query.RegionId.HasValue ? $"&regionId={query.RegionId}" : "") +
                (query.SalesGroupId.HasValue ? $"&salesGroupId={query.SalesGroupId}" : "");
            // One tile for the whole redemption queue; the per-status split lives on the Đổi quà page.
            var openRedemptions = redemptionQuery.Where(x => x.Status == "Pending" || x.Status == "Approved" || x.Status == "Delivered");
            var redemptionCounts = await openRedemptions.GroupBy(x => x.Status)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var agedRedemptions = await openRedemptions.CountAsync(x => x.UpdatedAt < now.AddDays(-3), ct);
            cards.Add(new("redemptionOpen", "Đổi quà cần xử lý", redemptionCounts.Values.Sum(), "count",
                $"Chờ duyệt {redemptionCounts.GetValueOrDefault("Pending")} · chờ giao {redemptionCounts.GetValueOrDefault("Approved")} · " +
                $"chờ xác nhận {redemptionCounts.GetValueOrDefault("Delivered")} · quá 3 ngày {agedRedemptions}",
                agedRedemptions > 0 ? "danger" : "warn", $"/redeem{(redemptionScope.Length > 0 ? "?" + redemptionScope[1..] : "")}"));
            var redemptionWork = await (from x in openRedemptions join r in db.RetailerRecords on x.RetailerId equals r.Id
                join gift in db.LoyaltyRewardRecords on x.LoyaltyRewardId equals gift.Id
                orderby x.UpdatedAt select new { x.Id, x.Status, x.UpdatedAt, r.Name, Gift = gift.Name }).Take(WorkItemLimit).ToArrayAsync(ct);
            work.AddRange(redemptionWork.Select(x => new DashboardWorkItem("redemption", x.Id, x.Id.ToString(),
                $"{x.Name} · {x.Gift}", x.Status, x.Status == "Pending" ? "Chờ duyệt đổi quà" : x.Status == "Approved" ? "Chờ giao quà" : "Chờ xác nhận nhận quà",
                x.UpdatedAt, x.UpdatedAt < now.AddDays(-3) ? 1 : 2, $"/redeem/{x.Id}")));
            notes.Add("Hàng chờ đổi quà theo khu vực/nhóm Sales, không phụ thuộc kho hoặc kỳ ngày.");
        }

        if (!keeper)
        {
            statuses = await periodOrders.GroupBy(o => o.Status).Select(g => new DashboardStatus(g.Key, g.Count())).ToArrayAsync(ct);
            var recognizedQuery = Recognized(orders, start, end, periodFrom, periodTo);
            var recognized = await recognizedQuery.ToArrayAsync(ct);
            // Same-length period immediately before, for the comparison shown on the revenue tiles.
            var span = periodTo.DayNumber - periodFrom.DayNumber + 1;
            var previousFrom = periodFrom.AddDays(-span); var previousTo = periodFrom.AddDays(-1);
            var previousAmounts = await Recognized(orders, start.AddDays(-span), start, previousFrom, previousTo)
                .Select(r => r.Amount).ToArrayAsync(ct);
            var previous = new { Count = previousAmounts.Length, Amount = previousAmounts.Sum() };
            completed = recognized.Select(r => new CompletedRow(r.Id, r.CreatedByUserId, r.SalesGroupId, r.RetailerId, r.CreatorRole,
                r.Amount, r.At.HasValue ? DateOnly.FromDateTime(r.At.Value.ToOffset(Vietnam).DateTime) :
                    r.Delivered ?? DateOnly.FromDateTime(r.ServerReceivedAt.ToOffset(Vietnam).DateTime))).ToArray();
            if (recognized.Any(r => !r.At.HasValue)) notes.Add("Một số đơn cũ thiếu lịch sử hoàn tất: dùng ngày giao/ngày tiếp nhận theo quy tắc báo cáo hiện tại.");
            var totals = await payments.GroupBy(_ => 1).Select(g => new
            {
                Outstanding = g.Sum(p => p.Method == "Credit" && p.DeliveredDate != null ? Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount) : 0m),
                Overdue = g.Sum(p => p.Method == "Credit" && p.DeliveredDate != null && p.DueDate < today ? Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount) : 0m),
                Prepaid = g.Sum(p => p.Method == "Prepaid" ? Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount) : 0m),
            }).SingleOrDefaultAsync(ct);

            var receipts = db.PaymentReceiptRecords.AsNoTracking().Where(r => r.Status == PaymentReceiptStatus.Confirmed && r.ConfirmedAt >= start && r.ConfirmedAt < end);
            if (global && !query.RegionId.HasValue && !query.SalesGroupId.HasValue)
                collections = await receipts.Select(r => new CollectionRow(r.ConfirmedAt!.Value, r.ReceivedAmount)).ToArrayAsync(ct);
            else
            {
                collections = await (from allocation in db.PaymentReceiptAllocationRecords.AsNoTracking()
                    join receipt in receipts on allocation.PaymentReceiptId equals receipt.Id
                    join payment in payments on allocation.PaymentId equals payment.Id
                    select new CollectionRow(receipt.ConfirmedAt!.Value, allocation.Amount)).ToArrayAsync(ct);
                notes.Add("Tiền thu theo nhóm/khu vực gồm phần đã phân bổ vào đơn thuộc phạm vi; tiền dư chưa phân bổ chỉ nằm ở tổng toàn hệ thống.");
            }
            var legacyCollections = await payments.Where(p => p.ConfirmedAmount > 0 && p.ConfirmedAt >= start && p.ConfirmedAt < end &&
                !db.PaymentReceiptAllocationRecords.Any(a => a.PaymentId == p.Id &&
                    db.PaymentReceiptRecords.Any(r => r.Id == a.PaymentReceiptId && r.Status == PaymentReceiptStatus.Confirmed)))
                .Select(p => new CollectionRow(p.ConfirmedAt!.Value, p.ConfirmedAmount)).ToArrayAsync(ct);
            collections = collections.Concat(legacyCollections).ToArray();
            var revenueDays = completed.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            var collectionDays = collections.GroupBy(r => DateOnly.FromDateTime(r.At.ToOffset(Vietnam).DateTime)).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            trend = Enumerable.Range(0, periodTo.DayNumber - periodFrom.DayNumber + 1).Select(i => periodFrom.AddDays(i))
                .Select(d => new DashboardDay(d, revenueDays.GetValueOrDefault(d), collectionDays.GetValueOrDefault(d))).ToArray();
            var lineTotals = db.SalesOrderLineRecords.AsNoTracking().GroupBy(l => l.SalesOrderId)
                .Select(g => new { Id = g.Key, Net = g.Sum(l => l.NetAmount) });
            products = await (from line in db.SalesOrderLineRecords.AsNoTracking()
                join order in recognizedQuery on line.SalesOrderId equals order.Id
                join total in lineTotals on line.SalesOrderId equals total.Id
                group new { line, order, total } by new { line.ProductId, line.SkuSnapshot, line.ProductNameSnapshot } into g
                orderby g.Sum(r => r.line.OrderedQuantity) descending
                select new DashboardProduct(g.Key.ProductId, g.Key.SkuSnapshot, g.Key.ProductNameSnapshot,
                    g.Sum(r => r.line.OrderedQuantity), g.Sum(r => r.total.Net > 0 ? Math.Round(r.order.Amount * r.line.NetAmount / r.total.Net, 2) : 0m)))
                .Take(TopProductLimit).ToArrayAsync(ct);
            var roster = await (from member in db.SalesGroupMemberRecords.AsNoTracking()
                join person in db.UserAccountRecords.AsNoTracking() on member.UserAccountId equals person.Id
                join groupRow in db.SalesGroupRecords.AsNoTracking() on member.SalesGroupId equals groupRow.Id
                where member.IsActive && person.IsActive && person.Role == SystemRoles.Sales && allowedGroups.Contains(groupRow.Id)
                select new { person.Id, person.FullName, GroupId = groupRow.Id, groupRow.Code }).Distinct().ToArrayAsync(ct);
            team = roster.Select(p => { var rows = completed.Where(o => o.CreatorId == p.Id && o.GroupId == p.GroupId).ToArray();
                return new DashboardSalesPerson(p.Id, p.FullName, p.GroupId, p.Code, rows.Length, rows.Select(o => o.RetailerId).Distinct().Count(), rows.Sum(o => o.Amount)); })
                .OrderByDescending(p => p.Revenue).ThenBy(p => p.Name).ToArray();
            unattributed = completed.Sum(o => o.Amount) - team.Sum(s => s.Revenue);
            cards.Add(new("revenue", "Doanh thu ghi nhận", completed.Sum(o => o.Amount), "VND", "Đơn hoàn tất trong kỳ", "success",
                PreviousValue: previous?.Amount ?? 0m));
            cards.Add(new("completed", "Đơn hoàn tất", completed.Length, "count", "Theo ngày hoàn tất", PreviousValue: previous?.Count ?? 0));
            cards.Add(new("collected", "Tiền đã thu trong kỳ", collections.Sum(r => r.Amount), "VND", "Giao dịch thu đã xác nhận", "success"));
            cards.Add(new("outstanding", "Công nợ còn phải thu", totals?.Outstanding ?? 0m, "VND", "Đơn công nợ đã giao, chưa thu đủ"));
            cards.Add(new("overdue", "Công nợ quá hạn", totals?.Overdue ?? 0m, "VND", "Phần còn lại đã vượt hạn thu", "danger"));
            // Amount and order count of the same prepaid queue, in one tile.
            var prepaidOrders = await payments.CountAsync(p => p.Method == "Prepaid" && p.ConfirmedAmount < p.AdjustedAmount, ct);
            cards.Add(new("prepaid", "Trả trước đang chờ", totals?.Prepaid ?? 0m, "VND",
                $"{prepaidOrders} đơn chưa xác nhận đủ tiền · tách riêng khỏi công nợ"));

            // Credit receivables by how far past due, so collection effort goes to the oldest money first.
            var openCredit = await payments.Where(p => p.Method == "Credit" && p.DeliveredDate != null && p.AdjustedAmount > p.ConfirmedAmount)
                .Select(p => new { p.DueDate, Amount = p.AdjustedAmount - p.ConfirmedAmount }).ToArrayAsync(ct);
            string Bucket(DateOnly? due) => due is null || due.Value >= today ? "current"
                : today.DayNumber - due.Value.DayNumber <= 7 ? "d1_7" : today.DayNumber - due.Value.DayNumber <= 30 ? "d8_30" : "d31";
            debtAging = new[] { ("current", "Chưa đến hạn"), ("d1_7", "Quá hạn 1–7 ngày"), ("d8_30", "Quá hạn 8–30 ngày"), ("d31", "Quá hạn trên 30 ngày") }
                .Select(b => { var rows = openCredit.Where(p => Bucket(p.DueDate) == b.Item1).ToArray();
                    return new DashboardAgingBucket(b.Item1, b.Item2, rows.Length, rows.Sum(p => p.Amount)); }).ToArray();

            cards.Add(new("allocationIssues", "Phân bổ cần xử lý", await orders.CountAsync(o => o.Status == OrderStatuses.AllocationFailed || o.Status == OrderStatuses.AllocationConflict || o.Status == OrderStatuses.Allocating && o.UpdatedAt < now.AddMinutes(-2), ct), "count", "Lỗi, xung đột hoặc quá 2 phút", "danger"));
            cards.Add(new("quotesPending", "Báo giá chờ phản hồi", await orders.CountAsync(o => o.Status == OrderStatuses.Quoted, ct), "count", "Đang chờ Retailer xác nhận PO"));
            var requests = db.ReceiptDiscrepancyRecords.AsNoTracking().Where(c => orderIds.Contains(c.SalesOrderId) &&
                c.ResolvedAt == null && c.Status != "Rejected" && c.Status != "Cancelled");
            var openRequests = await requests.CountAsync(ct);
            var openReturns = await requests.CountAsync(c => c.RequestType != "Complaint", ct);
            cards.Add(new("complaintsOpen", "Yêu cầu đang mở", openRequests, "count",
                $"Khiếu nại {openRequests - openReturns} · trả hàng / thu hồi {openReturns}", "warn", manager ? null : "/complaints"));

            // Service quality for orders created in the period (allocation, quote) and parts received in it.
            var statusCount = statuses.ToDictionary(s => s.Status, s => s.Count);
            var decided = statuses.Where(s => s.Status != OrderStatuses.Allocating).Sum(s => s.Count);
            var failed = AllocationFailureStatuses.Sum(s => statusCount.GetValueOrDefault(s));
            cards.Add(new("allocationSuccess", "Tỷ lệ phân bổ thành công", Percent(decided - failed, decided), "percent",
                decided == 0 ? "Chưa có đơn phân bổ xong trong kỳ" : $"{failed}/{decided} đơn thất bại, hết hàng hoặc xung đột",
                failed > 0 ? "warn" : "success"));
            var accepted = QuoteAcceptedStatuses.Sum(s => statusCount.GetValueOrDefault(s));
            var declined = QuoteDeclinedStatuses.Sum(s => statusCount.GetValueOrDefault(s));
            cards.Add(new("quoteConversion", "Tỷ lệ báo giá thành PO", Percent(accepted, accepted + declined), "percent",
                accepted + declined == 0 ? "Chưa có báo giá được chốt trong kỳ" : $"{accepted} chấp nhận · {declined} từ chối hoặc hết hạn"));
            var orderEta = db.SalesOrderLineRecords.AsNoTracking().GroupBy(l => l.SalesOrderId)
                .Select(g => new { Id = g.Key, Eta = g.Max(l => l.EstimatedArrivalAt) });
            var orderShipments = db.ShipmentRecords.AsNoTracking().Where(s => orderIds.Contains(s.SalesOrderId));
            var receivedParts = await (from s in orderShipments.Where(s => s.ReceivedAt >= start && s.ReceivedAt < end)
                join eta in orderEta on s.SalesOrderId equals eta.Id into etaRows from eta in etaRows.DefaultIfEmpty()
                select new { s.TotalQuantity, s.ReceivedQuantity, s.ReceivedAt, Eta = eta == null ? null : eta.Eta }).ToArrayAsync(ct);
            var shipped = receivedParts.Sum(s => s.TotalQuantity);
            cards.Add(new("fillRate", "Tỷ lệ giao đủ hàng", Percent(receivedParts.Sum(s => s.ReceivedQuantity), shipped), "percent",
                shipped == 0 ? "Chưa có phần giao được nhận trong kỳ" : $"{receivedParts.Sum(s => s.ReceivedQuantity):#,0}/{shipped:#,0} thùng retailer xác nhận nhận"));
            var timed = receivedParts.Where(s => s.Eta.HasValue).ToArray();
            var onTime = timed.Count(s => s.ReceivedAt <= s.Eta);
            cards.Add(new("onTimeRate", "Tỷ lệ giao đúng hạn", Percent(onTime, timed.Length), "percent",
                timed.Length == 0 ? "Chưa có phần giao có ETA trong kỳ" : $"{onTime}/{timed.Length} phần giao nhận trước ETA",
                onTime < timed.Length ? "warn" : "success"));
            cards.Add(new("shipmentsLate", "Đang giao quá ETA", await (from s in orderShipments.Where(s => s.Status == "Delivering")
                join eta in orderEta on s.SalesOrderId equals eta.Id where eta.Eta < now select s.Id).CountAsync(ct),
                "count", "Đã rời kho, retailer chưa nhận, đã qua giờ dự kiến", "danger", manager ? null : "/orders"));
            var deliveredInPeriod = await payments.CountAsync(p => p.DeliveredDate >= periodFrom && p.DeliveredDate <= periodTo, ct);
            var reportedInPeriod = await db.ReceiptDiscrepancyRecords.AsNoTracking()
                .CountAsync(c => orderIds.Contains(c.SalesOrderId) && c.ReportedAt >= start && c.ReportedAt < end && c.Status != "Cancelled", ct);
            cards.Add(new("complaintRate", "Tỷ lệ khiếu nại", Percent(reportedInPeriod, deliveredInPeriod), "percent",
                $"{reportedInPeriod} yêu cầu / {deliveredInPeriod} đơn giao trong kỳ", reportedInPeriod > 0 ? "warn" : "success"));

            var failures = await orders.Where(o => o.Status == OrderStatuses.AllocationFailed || o.Status == OrderStatuses.AllocationConflict ||
                    o.Status == OrderStatuses.Allocating && o.UpdatedAt < now.AddMinutes(-2))
                .OrderBy(o => o.UpdatedAt).Take(WorkItemLimit).Select(o => new { o.Id, o.OrderCode, o.Status, o.RejectionReason, o.UpdatedAt }).ToArrayAsync(ct);
            work.AddRange(failures.Select(o => new DashboardWorkItem("allocation", o.Id, o.OrderCode, "Phân bổ cần xử lý", o.Status,
                o.RejectionReason ?? "Đang phân bổ quá 2 phút", o.UpdatedAt, 1, manager ? null : $"/orders/{o.Id}")));
            var complaints = await requests.OrderBy(c => c.ReportedAt).Take(WorkItemLimit)
                .Select(c => new { c.Id, c.ReportedAt, c.SalesOrderId, c.Status, c.RequestType }).ToArrayAsync(ct);
            work.AddRange(complaints.Select(c => new DashboardWorkItem("complaint", c.Id, $"KN-{c.Id.ToString("N")[..8].ToUpperInvariant()}",
                c.RequestType == "Return" ? "Yêu cầu trả hàng" : c.RequestType == "DamagedCollection" ? "Thu hồi hàng hỏng" : "Khiếu nại đang chờ",
                c.Status, "Cần đối chiếu phần giao, thu hồi và xử lý tài chính", c.ReportedAt, 2,
                manager ? null : $"/complaints/KN-{c.Id.ToString("N")[..8].ToUpperInvariant()}")));
            if (global)
            {
                var refunds = await db.ManualRefundRecords.AsNoTracking().Where(r => orderIds.Contains(r.SalesOrderId) && r.Status != "Refunded")
                    .OrderBy(r => r.CreatedAt).Take(WorkItemLimit).Select(r => new { r.Id, r.SalesOrderId, r.Status, r.Amount, r.CreatedAt }).ToArrayAsync(ct);
                work.AddRange(refunds.Select(r => new DashboardWorkItem("refund", r.Id, $"HT-{r.Id.ToString("N")[..8].ToUpperInvariant()}",
                    r.Status == "AwaitingVerification" ? "Hoàn tiền chờ xác minh" : "Hoàn tiền cần xử lý", r.Status,
                    "Chỉ trạng thái Refunded mới tính là đã hoàn", r.CreatedAt, r.Status == "AwaitingVerification" ? 1 : 2, $"/refunds/{r.Id}", r.Amount)));
            }
            // Background-job health is technical; only Admin sees it.
            if (role == SystemRoles.Admin)
            {
                var sweep = await db.ExpirySweepRunRecords.AsNoTracking().OrderByDescending(r => r.StartedAt)
                    .Select(r => new { r.StartedAt, r.Status }).FirstOrDefaultAsync(ct);
                health = new(sweep?.StartedAt, sweep?.Status,
                    await db.OutboxMessageRecords.CountAsync(m => m.Status == "Pending", ct),
                    await db.OutboxMessageRecords.CountAsync(m => m.Status == "Failed", ct),
                    await orders.CountAsync(o => o.Status == OrderStatuses.Allocating && o.UpdatedAt < now.AddMinutes(-2), ct));
            }
            if (manager || global)
            {
                var scopedRetailers = global
                    ? db.RetailerRecords.AsNoTracking().Where(r => (!query.RegionId.HasValue || r.RegionId == query.RegionId) &&
                        (!query.SalesGroupId.HasValue || db.RetailerSalesAssignmentRecords.Any(a =>
                            a.RetailerId == r.Id && a.SalesGroupId == query.SalesGroupId && a.IsActive && a.UnassignedAt == null)))
                    : db.RetailerRecords.AsNoTracking().Where(r => db.RetailerSalesAssignmentRecords.Any(a => a.RetailerId == r.Id && a.IsActive && allowedGroups.Contains(a.SalesGroupId)));

                // Purchase cycle: an active store is overdue when the gap since its last order exceeds
                // the interval implied by its declared frequency (30 / orders per month days).
                var cycleRows = await scopedRetailers.Where(r => r.Status == "Active" && r.PurchaseFrequencyPerMonth > 0)
                    .Select(r => new { r.Id, r.PurchaseFrequencyPerMonth, r.ReviewedAt, r.CreatedAt,
                        Last = orders.Where(o => o.RetailerId == r.Id).Max(o => (DateTimeOffset?)o.ServerReceivedAt) }).ToArrayAsync(ct);
                bool CycleOverdue(DateTimeOffset? last, DateTimeOffset since, int frequency) =>
                    now - (last ?? since) > TimeSpan.FromDays(30d / frequency);
                var overdueCycle = cycleRows.Where(r => CycleOverdue(r.Last, r.ReviewedAt ?? r.CreatedAt, r.PurchaseFrequencyPerMonth))
                    .Select(r => r.Id).ToHashSet();
                cards.Add(new("cycleOverdue", "Điểm bán quá chu kỳ mua", overdueCycle.Count, "count",
                    "Lâu chưa đặt so với tần suất mua đã khai báo", overdueCycle.Count > 0 ? "warn" : "neutral"));
                cards.Add(new("newRetailers", "Điểm bán mới được duyệt", await scopedRetailers.CountAsync(r =>
                    r.Status == "Active" && r.ReviewedAt >= start && r.ReviewedAt < end, ct), "count", "Duyệt trong kỳ đã chọn", "success",
                    manager ? null : "/retailers"));
                cards.Add(new("pendingDeclarations", "Khai báo chờ duyệt", await scopedRetailers.CountAsync(r => r.Status == "PendingApproval", ct),
                    "count", "Sales đã gửi, chờ Admin xét duyệt", "warn", manager ? null : "/retailers"));

                var customerRows = await scopedRetailers.Select(r => new { r.Id, r.Code, r.Name, r.Status, r.PurchaseFrequencyPerMonth,
                    Count = periodOrders.Count(o => o.RetailerId == r.Id),
                    Last = orders.Where(o => o.RetailerId == r.Id).Max(o => (DateTimeOffset?)o.ServerReceivedAt),
                    Outstanding = payments.Where(p => p.DeliveredDate != null && p.Method == "Credit" && orders.Any(o => o.Id == p.SalesOrderId && o.RetailerId == r.Id))
                        .Sum(p => (decimal?)Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount)) ?? 0m,
                    Overdue = payments.Where(p => p.DeliveredDate != null && p.Method == "Credit" && p.DueDate < today && orders.Any(o => o.Id == p.SalesOrderId && o.RetailerId == r.Id))
                        .Sum(p => (decimal?)Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount)) ?? 0m,
                }).OrderByDescending(r => r.Overdue).ThenByDescending(r => r.Outstanding).ThenBy(r => r.Last).Take(30).ToArrayAsync(ct);
                customers = customerRows.Select(r => new DashboardCustomer(r.Id, r.Code, r.Name, r.Status, r.Count, r.Last,
                    r.PurchaseFrequencyPerMonth, r.Outstanding, r.Overdue, overdueCycle.Contains(r.Id))).ToArray();
            }
        }

        if (!manager)
        {
            var stock = from batch in db.BatchRecords.AsNoTracking()
                join balance in db.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
                join warehouse in db.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
                join product in db.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
                where allowedWarehouses.Contains(batch.WarehouseId)
                let locked = db.InventoryLockRecords.Where(l => l.BatchId == batch.Id && l.Status == "Active").Sum(l => (decimal?)l.LockedQuantity) ?? 0m
                let available = batch.ExpirationDate < today ? 0m : Math.Max(0m, balance.PhysicalQuantity - balance.ReservedQuantity - locked)
                select new { batch.Id, batch.BatchCode, batch.ExpirationDate, warehouseId = warehouse.Id, warehouse.Code, warehouse.Name,
                    warehouse.RegionId, ProductId = product.Id,
                    product.Sku, product.NearDateThresholdHours, OnHand = balance.PhysicalQuantity, Reserved = balance.ReservedQuantity, Locked = locked, Available = available };
            var stockRows = await stock.ToArrayAsync(ct);
            var warehouseRows = stockRows.GroupBy(b => new { b.warehouseId, b.Code, b.Name });
            bool IsNear(DateOnly expiry, int threshold) => expiry >= today && expiry <= today.AddDays((int)Math.Ceiling(threshold / 24m));
            inventory = warehouseRows.Select(g => new DashboardWarehouse(g.Key.warehouseId, g.Key.Code, g.Key.Name,
                g.Sum(b => b.OnHand), g.Sum(b => b.Reserved), g.Sum(b => b.Locked), g.Sum(b => b.Available),
                g.Where(b => IsNear(b.ExpirationDate, b.NearDateThresholdHours)).Sum(b => b.OnHand),
                g.Where(b => b.ExpirationDate < today).Sum(b => b.OnHand))).OrderBy(w => w.Code).ToArray();
            inventory = warehouses.Where(w => allowedWarehouses.Contains(w.Id)).Select(w => inventory.FirstOrDefault(i => i.Id == w.Id)
                ?? new DashboardWarehouse(w.Id, w.Code, w.Name, 0, 0, 0, 0, 0, 0)).OrderBy(w => w.Code).ToArray();
            var riskRows = stockRows.Where(b => b.OnHand > 0 && (b.ExpirationDate < today || IsNear(b.ExpirationDate, b.NearDateThresholdHours))).ToArray();
            expiryRiskTotal = riskRows.Length;
            risks = riskRows
                .OrderBy(b => b.ExpirationDate).ThenByDescending(b => b.OnHand).Take(ExpiryRiskLimit)
                .Select(b => new DashboardExpiry(b.Id, b.BatchCode, b.Code, b.Sku, b.ExpirationDate, b.OnHand, b.Available,
                    b.ExpirationDate < today ? "Expired" : "NearExpiry")).ToArray();
            var picks = db.PickingListRecords.AsNoTracking().Where(p => allowedWarehouses.Contains(p.WarehouseId) && p.Status != "Completed");
            var shipments = db.ShipmentRecords.AsNoTracking().Where(s => allowedWarehouses.Contains(s.WarehouseId));
            var receiptRows = db.GoodsReceiptRecords.AsNoTracking().Where(r => allowedWarehouses.Contains(r.WarehouseId) && r.Status == GoodsReceiptStatuses.Pending);
                cards.Add(new("receiptPending", "Phiếu nhập chờ xử lý", await receiptRows.CountAsync(ct), "count", "Toàn bộ phiếu còn mở"));
                cards.Add(new("pickingPending", "Phiếu chờ soạn", await picks.CountAsync(p => p.Status == "Pending", ct), "count", "Kho được phân công"));
                cards.Add(new("pickingActive", "Phiếu đang soạn", await picks.CountAsync(p => p.Status == "InProgress", ct), "count", "Chưa hoàn tất"));
                cards.Add(new("nearExpiry", "Hàng cận hạn", inventory.Sum(w => w.NearExpiry), "carton", "Theo ngưỡng cận hạn từng SKU", "warn"));
                cards.Add(new("expired", "Hàng hết hạn", inventory.Sum(w => w.Expired), "carton", "Tồn vật lý chưa xử lý", "danger"));
                cards.Add(new("shipmentsReady", "Shipment chờ xuất", await shipments.CountAsync(s => s.Status == "Ready", ct), "count", "Tại các kho trong bộ lọc · kiểm tra readiness trước xuất"));

            // Stock cover per SKU and region: regional minimum, recent sales pace and shelf life.
            var minimumRaw = await db.SystemConfigurationRecords.AsNoTracking()
                .Where(c => c.ConfigKey == BusinessConfigurationKeys.RegionalMinimumStockCartons && c.IsActive)
                .OrderByDescending(c => c.EffectiveFrom).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
            var minimum = decimal.TryParse(minimumRaw?.Trim('"'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var configured) && configured >= 0 ? configured : 35m;
            var salesSince = now.AddDays(-30);
            var sold = await db.InventoryTransactionRecords.AsNoTracking()
                .Where(t => t.TransactionType == InventoryTransactionTypes.SalesIssue && allowedWarehouses.Contains(t.WarehouseId) && t.OccurredAt >= salesSince)
                .Join(db.WarehouseRecords.AsNoTracking(), t => t.WarehouseId, w => w.Id, (t, w) => new { t.ProductId, w.RegionId, t.PhysicalQuantityDelta })
                .GroupBy(t => new { t.ProductId, t.RegionId }).Select(g => new { g.Key.ProductId, g.Key.RegionId, Qty = -g.Sum(t => t.PhysicalQuantityDelta) })
                .ToDictionaryAsync(x => (x.ProductId, x.RegionId), x => x.Qty, ct);
            var scopeRegions = warehouses.Where(w => allowedWarehouses.Contains(w.Id)).Select(w => w.RegionId!.Value).Distinct().ToArray();
            var regionCodes = await db.RegionRecords.AsNoTracking().Where(r => scopeRegions.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, ct);
            var publishedProducts = await db.ProductRecords.AsNoTracking().Where(p => p.Status == "Published")
                .Select(p => new { p.Id, p.Sku, p.Name, p.ShelfLifeHours }).ToArrayAsync(ct);
            var availableBySkuRegion = stockRows.GroupBy(b => (b.ProductId, b.RegionId)).ToDictionary(g => g.Key, g => g.Sum(b => b.Available));
            var coverRows = new List<DashboardStockCover>();
            foreach (var regionId in scopeRegions)
            foreach (var product in publishedProducts)
            {
                var available = availableBySkuRegion.GetValueOrDefault((product.Id, regionId));
                var soldQty = Math.Max(0m, sold.GetValueOrDefault((product.Id, regionId)));
                // Products neither stocked nor sold in a region are not part of its assortment.
                if (available <= 0 && soldQty <= 0) continue;
                var daily = soldQty / 30m;
                decimal? cover = daily > 0 ? Math.Round(available / daily, 1) : null;
                var shelfDays = (int)Math.Ceiling(product.ShelfLifeHours / 24m);
                var flag = available < minimum ? "BelowMinimum"
                    : soldQty <= 0 ? "NoSales"
                    : cover > shelfDays ? "Overstock" : null;
                if (flag is null) continue;
                coverRows.Add(new(product.Id, product.Sku, product.Name, regionId, regionCodes.GetValueOrDefault(regionId, ""),
                    available, soldQty, cover, shelfDays, minimum, flag));
            }
            stockCover = coverRows.OrderBy(r => r.Flag == "BelowMinimum" ? 0 : r.Flag == "Overstock" ? 1 : 2)
                .ThenBy(r => r.Flag == "BelowMinimum" ? r.Available : -(r.DaysOfSupply ?? decimal.MaxValue))
                .ThenBy(r => r.Sku, StringComparer.Ordinal).Take(20).ToArray();
            // Not shown on the Portal tiles; the mobile warehouse home uses it for its low-stock banner.
            cards.Add(new("lowStock", "SKU dưới ngưỡng tồn khu vực", coverRows.Count(r => r.Flag == "BelowMinimum"), "count",
                $"Ngưỡng tối thiểu {minimum:0.#} thùng / SKU / khu vực", coverRows.Any(r => r.Flag == "BelowMinimum") ? "danger" : "neutral"));
                cards.Add(new("shipmentExceptions", "Shipment ngoại lệ", await shipments.CountAsync(s => s.Status == "Exception", ct), "count", "Cần kiểm tra trước khi tiếp tục", "danger"));
            var pickRows = await picks.OrderByDescending(p => p.Status == "Exception").ThenBy(p => p.CreatedAt).Take(WorkItemLimit).Select(p => new { p.Id, p.PickingCode, p.Status, p.CreatedAt }).ToArrayAsync(ct);
            work.AddRange(pickRows.Select(p => new DashboardWorkItem("picking", p.Id, p.PickingCode, "Phiếu soạn còn mở", p.Status,
                "Mở phiếu để tiếp tục soạn / xử lý ngoại lệ", p.CreatedAt, p.Status == "Exception" ? 1 : 3, $"/runpicking?code={Uri.EscapeDataString(p.PickingCode)}")));
            var shipmentRows = await shipments.Where(s => s.Status == "Exception" || s.Status == "Ready" || s.Status == "Issued")
                .OrderByDescending(s => s.Status == "Exception").ThenBy(s => s.CreatedAt).Take(WorkItemLimit).Select(s => new { s.Id, s.ShipmentCode, s.Status, s.ExceptionReason, s.CreatedAt }).ToArrayAsync(ct);
            work.AddRange(shipmentRows.Select(s => new DashboardWorkItem("shipment", s.Id, s.ShipmentCode,
                s.Status == "Exception" ? "Shipment ngoại lệ" : s.Status == "Issued" ? "Chờ điều phối giao" : "Sẵn sàng xuất kho", s.Status,
                s.ExceptionReason ?? "Kiểm tra kho, thanh toán và điều phối", s.CreatedAt, s.Status == "Exception" ? 1 : 2, "/readiness")));
                var receiptsOpen = await receiptRows.OrderBy(r => r.CreatedAt).Take(WorkItemLimit).Select(r => new { r.Id, r.ReceiptCode, r.Status, r.CreatedAt }).ToArrayAsync(ct);
                work.AddRange(receiptsOpen.Select(r => new DashboardWorkItem("receipt", r.Id, r.ReceiptCode, "Phiếu nhập còn mở", r.Status,
                    "Kiểm tra SKU, batch và số lượng nhập", r.CreatedAt, 3, $"/receipts/{Uri.EscapeDataString(r.ReceiptCode)}")));
            var transferArrivals = await db.WarehouseDocuments.AsNoTracking()
                .Where(d => d.Kind == "Issue" && d.Reason == "Transfer" && d.Status == "Posted"
                    && d.DestinationWarehouseId.HasValue && allowedWarehouses.Contains(d.DestinationWarehouseId.Value)
                    && !db.WarehouseDocuments.Any(r => r.SourceDocumentId == d.Id && r.Status != "Cancelled"))
                .OrderBy(d => d.PostedAt ?? d.CreatedAt).Take(WorkItemLimit)
                .Select(d => new { d.Id, d.Code, At = d.PostedAt ?? d.CreatedAt }).ToArrayAsync(ct);
            cards.Add(new("transferArrivalPending", "Luân chuyển chờ nhận", transferArrivals.Length, "count", "Hàng đã xuất từ kho gửi, chờ nhập vào kho đích", transferArrivals.Length > 0 ? "warn" : "neutral", "/warehouse-transfers"));
            work.AddRange(transferArrivals.Select(t => new DashboardWorkItem("transfer", t.Id, t.Code, "Hàng luân chuyển chờ nhận", "Confirmed",
                "Mở phiếu để quét xác nhận nhập kho đích", t.At, 2, "/warehouse-transfers")));
            var expiredDisposal = riskRows.Where(b => b.ExpirationDate < today && b.OnHand > 0)
                .OrderBy(b => b.ExpirationDate).ThenByDescending(b => b.OnHand).Take(20).ToArray();
            work.AddRange(expiredDisposal.Select(b => new DashboardWorkItem("disposal", b.Id, b.BatchCode, "Lô hết hạn cần hủy", "Expired",
                $"Kho {b.Code} · SKU {b.Sku} · Tồn {b.OnHand:0.#} thùng", new DateTimeOffset(b.ExpirationDate.ToDateTime(TimeOnly.MinValue), Vietnam),
                1, "/disposal/new")));
        }
        return new(role, now, periodFrom, periodTo, scope, cards, statuses, trend, products, team, inventory, risks,
            work.OrderBy(w => w.Priority).ThenBy(w => w.Since).Take(WorkItemLimit).ToArray(), customers, health, unattributed, notes,
            debtAging, stockCover, expiryRiskTotal);
    }
}
