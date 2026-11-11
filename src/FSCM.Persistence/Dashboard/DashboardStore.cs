using FSCM.Application.Features.Dashboard;
using FSCM.Application.Features.Reports;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Inventory;
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
        decimal? unattributed = null;
        var orders = db.SalesOrderRecords.AsNoTracking().Where(o => !keeper && allowedGroups.Contains(o.SalesGroupId));
        var periodOrders = orders.Where(o => o.ServerReceivedAt >= start && o.ServerReceivedAt < end);
        var orderIds = orders.Select(o => o.Id);
        var payments = db.PaymentRecords.AsNoTracking().Where(p => orderIds.Contains(p.SalesOrderId) && p.Status != "Cancelled");
        var completed = Array.Empty<CompletedRow>(); var collections = Array.Empty<CollectionRow>();

        if (!keeper)
        {
            statuses = await periodOrders.GroupBy(o => o.Status).Select(g => new DashboardStatus(g.Key, g.Count())).ToArrayAsync(ct);
            var completionDates = db.OrderStatusHistoryRecords.AsNoTracking().Where(h => h.ToStatus == OrderStatuses.Completed)
                .GroupBy(h => h.SalesOrderId).Select(g => new { Id = g.Key, At = (DateTimeOffset?)g.Max(h => h.ChangedAt) });
            var recognizedQuery = from order in orders.Where(o => o.Status == OrderStatuses.Completed)
                join payment in db.PaymentRecords.AsNoTracking() on order.Id equals payment.SalesOrderId into paymentRows
                from payment in paymentRows.DefaultIfEmpty()
                join date in completionDates on order.Id equals date.Id into dateRows
                from date in dateRows.DefaultIfEmpty()
                join creator in db.UserAccountRecords.AsNoTracking() on order.CreatedByUserId equals creator.Id
                where date.At != null ? date.At >= start && date.At < end :
                    payment != null && payment.DeliveredDate != null ? payment.DeliveredDate >= periodFrom && payment.DeliveredDate <= periodTo :
                    order.ServerReceivedAt >= start && order.ServerReceivedAt < end
                select new { order.Id, order.CreatedByUserId, order.SalesGroupId, order.RetailerId, CreatorRole = creator.Role,
                    Amount = payment != null ? payment.AdjustedAmount : order.FinalAmount,
                    order.FinalAmount, date.At,
                    Delivered = payment == null ? (DateOnly?)null : payment.DeliveredDate, order.ServerReceivedAt };
            var recognized = await recognizedQuery.ToArrayAsync(ct);
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
                .Take(10).ToArrayAsync(ct);
            var roster = await (from member in db.SalesGroupMemberRecords.AsNoTracking()
                join person in db.UserAccountRecords.AsNoTracking() on member.UserAccountId equals person.Id
                join groupRow in db.SalesGroupRecords.AsNoTracking() on member.SalesGroupId equals groupRow.Id
                where member.IsActive && person.IsActive && person.Role == SystemRoles.Sales && allowedGroups.Contains(groupRow.Id)
                select new { person.Id, person.FullName, GroupId = groupRow.Id, groupRow.Code }).Distinct().ToArrayAsync(ct);
            team = roster.Select(p => { var rows = completed.Where(o => o.CreatorId == p.Id && o.GroupId == p.GroupId).ToArray();
                return new DashboardSalesPerson(p.Id, p.FullName, p.GroupId, p.Code, rows.Length, rows.Select(o => o.RetailerId).Distinct().Count(), rows.Sum(o => o.Amount)); })
                .OrderByDescending(p => p.Revenue).ThenBy(p => p.Name).ToArray();
            unattributed = completed.Sum(o => o.Amount) - team.Sum(s => s.Revenue);
            cards.Add(new("revenue", "Doanh thu ghi nhận", completed.Sum(o => o.Amount), "VND", "Đơn hoàn tất trong kỳ", "success"));
            cards.Add(new("completed", "Đơn hoàn tất", completed.Length, "count", "Theo ngày hoàn tất"));
            cards.Add(new("collected", "Tiền đã thu trong kỳ", collections.Sum(r => r.Amount), "VND", "Giao dịch thu đã xác nhận", "success"));
            cards.Add(new("outstanding", "Công nợ còn phải thu", totals?.Outstanding ?? 0m, "VND", "Đơn công nợ đã giao, chưa thu đủ"));
            cards.Add(new("overdue", "Công nợ quá hạn", totals?.Overdue ?? 0m, "VND", "Phần còn lại đã vượt hạn thu", "danger"));
            cards.Add(new("prepaid", "Trả trước đang chờ", totals?.Prepaid ?? 0m, "VND", "Tách riêng khỏi công nợ"));
            cards.Add(new("allocationIssues", "Phân bổ cần xử lý", await orders.CountAsync(o => o.Status == OrderStatuses.AllocationFailed || o.Status == OrderStatuses.AllocationConflict || o.Status == OrderStatuses.Allocating && o.UpdatedAt < now.AddMinutes(-2), ct), "count", "Lỗi, xung đột hoặc quá 2 phút", "danger"));
            cards.Add(new("quotesPending", "Báo giá chờ phản hồi", await orders.CountAsync(o => o.Status == OrderStatuses.Quoted, ct), "count", "Đang chờ Retailer xác nhận PO"));
            cards.Add(new("prepaidOrders", "Đơn chờ trả trước", await payments.CountAsync(p => p.Method == "Prepaid" && p.ConfirmedAmount < p.AdjustedAmount, ct), "count", "Chưa xác nhận đủ tiền"));
            cards.Add(new("complaintsOpen", "Khiếu nại đang mở", await db.ReceiptDiscrepancyRecords.CountAsync(c => orderIds.Contains(c.SalesOrderId) && c.ResolvedAt == null, ct), "count", "Chưa đối soát xong", "warn"));

            var failures = await orders.Where(o => o.Status == OrderStatuses.AllocationFailed || o.Status == OrderStatuses.AllocationConflict ||
                    o.Status == OrderStatuses.Allocating && o.UpdatedAt < now.AddMinutes(-2))
                .OrderBy(o => o.UpdatedAt).Take(15).Select(o => new { o.Id, o.OrderCode, o.Status, o.RejectionReason, o.UpdatedAt }).ToArrayAsync(ct);
            work.AddRange(failures.Select(o => new DashboardWorkItem("allocation", o.Id, o.OrderCode, "Phân bổ cần xử lý", o.Status,
                o.RejectionReason ?? "Đang phân bổ quá 2 phút", o.UpdatedAt, 1, manager ? null : $"/orders/{o.Id}")));
            var complaints = await db.ReceiptDiscrepancyRecords.AsNoTracking().Where(c => orderIds.Contains(c.SalesOrderId) && c.ResolvedAt == null)
                .OrderBy(c => c.ReportedAt).Take(15).Select(c => new { c.Id, c.ReportedAt, c.SalesOrderId, c.Status }).ToArrayAsync(ct);
            work.AddRange(complaints.Select(c => new DashboardWorkItem("complaint", c.Id, $"KN-{c.Id.ToString("N")[..8].ToUpperInvariant()}",
                "Khiếu nại đang chờ", c.Status, "Cần đối chiếu phần giao và bằng chứng", c.ReportedAt, 2,
                manager ? null : $"/complaints/KN-{c.Id.ToString("N")[..8].ToUpperInvariant()}")));
            if (global)
            {
                var refunds = await db.ManualRefundRecords.AsNoTracking().Where(r => orderIds.Contains(r.SalesOrderId) && r.Status != "Refunded")
                    .OrderBy(r => r.CreatedAt).Take(15).Select(r => new { r.Id, r.SalesOrderId, r.Status, r.Amount, r.CreatedAt }).ToArrayAsync(ct);
                work.AddRange(refunds.Select(r => new DashboardWorkItem("refund", r.Id, $"HT-{r.Id.ToString("N")[..8].ToUpperInvariant()}",
                    r.Status == "AwaitingVerification" ? "Hoàn tiền chờ xác minh" : "Hoàn tiền cần xử lý", r.Status,
                    "Chỉ trạng thái Refunded mới tính là đã hoàn", r.CreatedAt, r.Status == "AwaitingVerification" ? 1 : 2, $"/refunds/{r.Id}", r.Amount)));
                var sweep = await db.ExpirySweepRunRecords.AsNoTracking().OrderByDescending(r => r.StartedAt)
                    .Select(r => new { r.StartedAt, r.Status }).FirstOrDefaultAsync(ct);
                health = new(sweep?.StartedAt, sweep?.Status,
                    await db.OutboxMessageRecords.CountAsync(m => m.Status == "Pending", ct),
                    await db.OutboxMessageRecords.CountAsync(m => m.Status == "Failed", ct),
                    await orders.CountAsync(o => o.Status == OrderStatuses.Allocating && o.UpdatedAt < now.AddMinutes(-2), ct));
            }
            if (manager)
            {
                var scopedRetailers = db.RetailerRecords.AsNoTracking().Where(r => db.RetailerSalesAssignmentRecords.Any(a => a.RetailerId == r.Id && a.IsActive && allowedGroups.Contains(a.SalesGroupId)));
                var customerRows = await scopedRetailers.Select(r => new { r.Id, r.Code, r.Name, r.Status, r.PurchaseFrequencyPerMonth,
                    Count = periodOrders.Count(o => o.RetailerId == r.Id),
                    Last = orders.Where(o => o.RetailerId == r.Id).Max(o => (DateTimeOffset?)o.ServerReceivedAt),
                    Outstanding = payments.Where(p => p.DeliveredDate != null && p.Method == "Credit" && orders.Any(o => o.Id == p.SalesOrderId && o.RetailerId == r.Id))
                        .Sum(p => (decimal?)Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount)) ?? 0m,
                    Overdue = payments.Where(p => p.DeliveredDate != null && p.Method == "Credit" && p.DueDate < today && orders.Any(o => o.Id == p.SalesOrderId && o.RetailerId == r.Id))
                        .Sum(p => (decimal?)Math.Max(0m, p.AdjustedAmount - p.ConfirmedAmount)) ?? 0m,
                }).OrderByDescending(r => r.Overdue).ThenBy(r => r.Last).Take(30).ToArrayAsync(ct);
                customers = customerRows.Select(r => new DashboardCustomer(r.Id, r.Code, r.Name, r.Status, r.Count, r.Last,
                    r.PurchaseFrequencyPerMonth, r.Outstanding, r.Overdue)).ToArray();
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
            risks = stockRows.Where(b => b.OnHand > 0 && (b.ExpirationDate < today || IsNear(b.ExpirationDate, b.NearDateThresholdHours)))
                .OrderBy(b => b.ExpirationDate).ThenByDescending(b => b.OnHand).Take(15)
                .Select(b => new DashboardExpiry(b.Id, b.BatchCode, b.Code, b.Sku, b.ExpirationDate, b.OnHand, b.Available,
                    b.ExpirationDate < today ? "Expired" : "NearExpiry")).ToArray();
            var picks = db.PickingListRecords.AsNoTracking().Where(p => allowedWarehouses.Contains(p.WarehouseId) && p.Status != "Completed");
            var shipments = db.ShipmentRecords.AsNoTracking().Where(s => allowedWarehouses.Contains(s.WarehouseId));
            var receiptRows = db.GoodsReceiptRecords.AsNoTracking().Where(r => allowedWarehouses.Contains(r.WarehouseId) && r.Status == GoodsReceiptStatuses.Pending);
                cards.Add(new("receiptPending", "Phiếu nhập chờ xử lý", await receiptRows.CountAsync(ct), "count", "Toàn bộ phiếu còn mở"));
                cards.Add(new("pickingPending", "Phiếu chờ soạn", await picks.CountAsync(p => p.Status == "Pending", ct), "count", "Kho được phân công"));
                cards.Add(new("pickingActive", "Phiếu đang soạn", await picks.CountAsync(p => p.Status == "InProgress", ct), "count", "Chưa hoàn tất"));
                cards.Add(new("ready", "Shipment chờ xuất", await shipments.CountAsync(s => s.Status == "Ready", ct), "count", "Kiểm tra readiness trước xuất"));
                cards.Add(new("nearExpiry", "Hàng cận hạn", inventory.Sum(w => w.NearExpiry), "carton", "Theo ngưỡng cận hạn từng SKU", "warn"));
                cards.Add(new("expired", "Hàng hết hạn", inventory.Sum(w => w.Expired), "carton", "Tồn vật lý chưa xử lý", "danger"));
                cards.Add(new("shipmentsReady", "Shipment chờ xuất", await shipments.CountAsync(s => s.Status == "Ready", ct), "count", "Tại các kho trong bộ lọc"));
                cards.Add(new("shipmentExceptions", "Shipment ngoại lệ", await shipments.CountAsync(s => s.Status == "Exception", ct), "count", "Cần kiểm tra trước khi tiếp tục", "danger"));
            var pickRows = await picks.OrderByDescending(p => p.Status == "Exception").ThenBy(p => p.CreatedAt).Take(15).Select(p => new { p.Id, p.PickingCode, p.Status, p.CreatedAt }).ToArrayAsync(ct);
            work.AddRange(pickRows.Select(p => new DashboardWorkItem("picking", p.Id, p.PickingCode, "Phiếu soạn còn mở", p.Status,
                "Mở phiếu để tiếp tục soạn / xử lý ngoại lệ", p.CreatedAt, p.Status == "Exception" ? 1 : 3, $"/runpicking?code={Uri.EscapeDataString(p.PickingCode)}")));
            var shipmentRows = await shipments.Where(s => s.Status == "Exception" || s.Status == "Ready" || s.Status == "Issued")
                .OrderByDescending(s => s.Status == "Exception").ThenBy(s => s.CreatedAt).Take(20).Select(s => new { s.Id, s.ShipmentCode, s.Status, s.ExceptionReason, s.CreatedAt }).ToArrayAsync(ct);
            work.AddRange(shipmentRows.Select(s => new DashboardWorkItem("shipment", s.Id, s.ShipmentCode,
                s.Status == "Exception" ? "Shipment ngoại lệ" : s.Status == "Issued" ? "Chờ điều phối giao" : "Sẵn sàng xuất kho", s.Status,
                s.ExceptionReason ?? "Kiểm tra kho, thanh toán và điều phối", s.CreatedAt, s.Status == "Exception" ? 1 : 2, "/readiness")));
                var receiptsOpen = await receiptRows.OrderBy(r => r.CreatedAt).Take(10).Select(r => new { r.Id, r.ReceiptCode, r.Status, r.CreatedAt }).ToArrayAsync(ct);
                work.AddRange(receiptsOpen.Select(r => new DashboardWorkItem("receipt", r.Id, r.ReceiptCode, "Phiếu nhập còn mở", r.Status,
                    "Kiểm tra SKU, batch và số lượng nhập", r.CreatedAt, 3, $"/receipts/{Uri.EscapeDataString(r.ReceiptCode)}")));
        }
        return new(role, now, periodFrom, periodTo, scope, cards, statuses, trend, products, team, inventory, risks,
            work.OrderBy(w => w.Priority).ThenBy(w => w.Since).Take(30).ToArray(), customers, health, unattributed, notes);
    }
}
