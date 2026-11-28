using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Reports;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

public sealed partial class BusinessReportStore
{
    private sealed record PromotionReportPrice(Guid Id, DateTimeOffset AcceptedAt, Guid OrderId, string OrderCode,
        string OrderStatus, Guid RetailerId, string RetailerCode, string RetailerName, string RegionName,
        string? CampaignCode, string? CampaignName, decimal BaseNetUnitPrice, decimal ListUnitPrice,
        decimal ShelfLifeDiscountPerUnit, int DiscountTableVersion, string SkuSnapshot, string ProductNameSnapshot,
        string BatchCode, decimal Quantity, decimal Amount, string? Explanation);

    public async Task<PromotionsReportResult> GetPromotionsAsync(
        PromotionsReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var (start, end) = PeriodBounds(q.FromDate, q.ToDate);
        var (groupSet, scope) = await ResolveGroupScopeAsync(null, actorUserId, actorRole, ct);

        var quotedLines =
            from quote in db.SalesOrderQuoteRecords.AsNoTracking()
            where quote.AcceptedAt != null
                && quote.AcceptedAt >= start && quote.AcceptedAt < end
                && !db.SalesOrderQuoteRecords.Any(other =>
                    other.SalesOrderId == quote.SalesOrderId && other.AcceptedAt != null && other.RevisionNumber > quote.RevisionNumber)
            join order in db.SalesOrderRecords.AsNoTracking() on quote.SalesOrderId equals order.Id
            join line in db.SalesOrderLineRecords.AsNoTracking() on order.Id equals line.SalesOrderId
            join r in db.RetailerRecords.AsNoTracking() on order.RetailerId equals r.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            where !q.RegionId.HasValue || r.RegionId == q.RegionId.Value
            where groupSet == null || groupSet.Contains(order.SalesGroupId)
            let campaignCode = db.OrderPromotionRecords
                .Where(p => p.SalesOrderLineId == line.Id && p.AppliedAt <= quote.AcceptedAt)
                .OrderByDescending(p => p.AppliedAt).Select(p => p.PromotionCodeSnapshot).FirstOrDefault()
            let campaignName = db.OrderPromotionRecords
                .Where(p => p.SalesOrderLineId == line.Id && p.AppliedAt <= quote.AcceptedAt)
                .OrderByDescending(p => p.AppliedAt).Select(p => p.PromotionNameSnapshot).FirstOrDefault()
            select new { Quote = quote, Order = order, Line = line, RegionName = reg != null ? reg.Name : "", campaignCode, campaignName };

        var planPrices = await (
            from x in quotedLines
            join price in db.AllocationPlanPriceRecords.AsNoTracking() on x.Quote.AllocationRunId equals price.AllocationRunId
            join frozenLine in db.AllocationRunLineRecords.AsNoTracking() on price.AllocationRunLineId equals frozenLine.Id
            join batch in db.BatchRecords.AsNoTracking() on price.BatchId equals batch.Id
            where frozenLine.SalesOrderLineId == x.Line.Id && price.ListUnitPrice > price.FinalNetUnitPrice
            select new PromotionReportPrice(price.Id, x.Quote.AcceptedAt!.Value, x.Order.Id, x.Order.OrderCode, x.Order.Status,
                x.Order.RetailerId, x.Order.RetailerCodeSnapshot, x.Order.RetailerNameSnapshot, x.RegionName,
                x.campaignCode, x.campaignName, price.BaseNetUnitPrice, price.ListUnitPrice, price.ShelfLifeDiscountPerUnit,
                price.DiscountTableVersion, x.Line.SkuSnapshot, x.Line.ProductNameSnapshot, batch.BatchCode, price.Quantity,
                price.Amount, price.Explanation)).ToArrayAsync(ct);

        // The reservation ledger ties a real-time allocation to its exact quote run, including after stock is released.
        var realTimePrices = await (
            from x in quotedLines
            join run in db.AllocationRunRecords.AsNoTracking() on x.Quote.AllocationRunId equals run.Id
            join allocation in db.AllocationRecords.AsNoTracking() on x.Line.Id equals allocation.SalesOrderLineId
            join batch in db.BatchRecords.AsNoTracking() on allocation.BatchId equals batch.Id
            where allocation.DiscountAmount > 0 && allocation.PostPromotionUnitPrice.HasValue
                && db.InventoryTransactionRecords.Any(entry => entry.SourceId == allocation.Id
                    && entry.SourceType == InventoryTransactionSourceTypes.Allocation
                    && entry.TransactionType == InventoryTransactionTypes.OrderReserve && entry.ReferenceCode == run.RunCode)
                && !db.AllocationPlanPriceRecords.Any(price => price.AllocationRunId == run.Id
                    && db.AllocationRunLineRecords.Any(line => line.Id == price.AllocationRunLineId && line.SalesOrderLineId == x.Line.Id))
            select new PromotionReportPrice(allocation.Id, x.Quote.AcceptedAt!.Value, x.Order.Id, x.Order.OrderCode, x.Order.Status,
                x.Order.RetailerId, x.Order.RetailerCodeSnapshot, x.Order.RetailerNameSnapshot, x.RegionName,
                x.campaignCode, x.campaignName, allocation.PostPromotionUnitPrice!.Value, allocation.UnitPrice,
                allocation.ShelfLifeDiscountPerUnit ?? 0, allocation.ShelfLifeDiscountTableVersion ?? 0, x.Line.SkuSnapshot,
                x.Line.ProductNameSnapshot, batch.BatchCode, allocation.AllocatedQuantity, allocation.NetAmount,
                allocation.PricingExplanation)).ToArrayAsync(ct);

        var retailerTerm = q.Retailer?.Trim().ToLowerInvariant();
        var programTerm = q.Program?.Trim().ToLowerInvariant();
        var exactCode = q.ExactProgramCode?.Trim();

        var mapped = planPrices.Concat(realTimePrices).Select(x =>
        {
            var hasCampaign = x.BaseNetUnitPrice < x.ListUnitPrice;
            var hasShelf = x.ShelfLifeDiscountPerUnit > 0;
            var discountType = hasCampaign && hasShelf ? "Combined" : hasCampaign ? "Campaign" : "ShelfLife";
            var programCode = hasCampaign
                ? (x.CampaignCode ?? "Chưa lưu mã") + (hasShelf ? $" + HSD-V{x.DiscountTableVersion}" : "")
                : $"HSD-V{x.DiscountTableVersion}";
            var programName = hasCampaign
                ? (x.CampaignName ?? "Khuyến mãi chưa lưu tên chiến dịch") + (hasShelf ? " + giảm theo HSD" : "")
                : "Giảm giá theo HSD";
            var gross = x.ListUnitPrice * x.Quantity;
            var totalDiscount = gross - x.Amount;
            var campaignDiscount = (x.ListUnitPrice - x.BaseNetUnitPrice) * x.Quantity;
            var shelfDiscount = x.ShelfLifeDiscountPerUnit * x.Quantity;

            return new
            {
                CampaignCode = x.CampaignCode,
                Item = new PromotionUsageItem(
                    x.Id, x.AcceptedAt, x.OrderId, x.OrderCode, x.OrderStatus,
                    x.RetailerId, x.RetailerCode, x.RetailerName, x.RegionName,
                    discountType, programCode, programName, x.SkuSnapshot, x.ProductNameSnapshot,
                    x.BatchCode, x.Quantity, x.ListUnitPrice, gross, campaignDiscount, shelfDiscount,
                    totalDiscount, x.Amount, x.Explanation ?? string.Empty)
            };
        })
        .Where(x => string.IsNullOrWhiteSpace(retailerTerm) ||
            x.Item.RetailerCode.ToLowerInvariant().Contains(retailerTerm) ||
            x.Item.RetailerName.ToLowerInvariant().Contains(retailerTerm))
        .Where(x => string.IsNullOrWhiteSpace(exactCode) ||
            string.Equals(x.CampaignCode, exactCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Item.ProgramCode, exactCode, StringComparison.OrdinalIgnoreCase))
        .Where(x => string.IsNullOrWhiteSpace(programTerm) ||
            x.Item.ProgramCode.ToLowerInvariant().Contains(programTerm) ||
            x.Item.ProgramName.ToLowerInvariant().Contains(programTerm) ||
            x.Item.Sku.ToLowerInvariant().Contains(programTerm) ||
            x.Item.ProductName.ToLowerInvariant().Contains(programTerm) ||
            x.Item.OrderCode.ToLowerInvariant().Contains(programTerm))
        .Where(x => string.IsNullOrWhiteSpace(q.DiscountType) || x.Item.DiscountType == q.DiscountType)
        .Where(x => !q.CompletedOnly || x.Item.OrderStatus == OrderStatuses.Completed)
        .Where(x => string.IsNullOrWhiteSpace(q.Status) || x.Item.OrderStatus == q.Status)
        .Select(x => x.Item)
        .OrderByDescending(x => x.AcceptedAt).ThenBy(x => x.Id)
        .ToArray();

        var totalGross = mapped.Sum(x => x.GrossAmount);
        var totalDiscountSum = mapped.Sum(x => x.TotalDiscountAmount);
        var summary = new PromotionsSummary(
            mapped.Select(x => x.OrderId).Distinct().Count(),
            mapped.Where(x => x.OrderStatus == OrderStatuses.Completed).Select(x => x.OrderId).Distinct().Count(),
            mapped.Sum(x => x.Quantity),
            totalGross,
            mapped.Sum(x => x.CampaignDiscountAmount),
            mapped.Sum(x => x.ShelfLifeDiscountAmount),
            totalDiscountSum,
            mapped.Sum(x => x.NetAmount),
            mapped.Where(x => x.OrderStatus == OrderStatuses.Completed).Sum(x => x.NetAmount),
            mapped.Where(x => x.OrderStatus == OrderStatuses.Completed).Sum(x => x.TotalDiscountAmount),
            totalGross <= 0 ? 0m : Math.Round(totalDiscountSum / totalGross * 100m, 2));

        var byProgram = mapped
            .GroupBy(x => new { x.ProgramCode, x.ProgramName })
            .Select(g =>
            {
                var gross = g.Sum(x => x.GrossAmount);
                var disc = g.Sum(x => x.TotalDiscountAmount);
                var types = g.Select(x => x.DiscountType).Distinct().ToArray();
                return new PromotionProgramSummaryRow(
                    g.Key.ProgramCode,
                    g.Key.ProgramName,
                    types.Length == 1 ? types[0] : "Combined",
                    g.Select(x => x.OrderId).Distinct().Count(),
                    g.Where(x => x.OrderStatus == OrderStatuses.Completed).Select(x => x.OrderId).Distinct().Count(),
                    g.Sum(x => x.Quantity),
                    gross,
                    disc,
                    g.Sum(x => x.CampaignDiscountAmount),
                    g.Sum(x => x.ShelfLifeDiscountAmount),
                    g.Sum(x => x.NetAmount),
                    g.Where(x => x.OrderStatus == OrderStatuses.Completed).Sum(x => x.TotalDiscountAmount),
                    g.Where(x => x.OrderStatus == OrderStatuses.Completed).Sum(x => x.NetAmount),
                    gross <= 0 ? 0m : Math.Round(disc / gross * 100m, 2));
            })
            .OrderByDescending(p => p.DiscountAmount)
            .ToArray();

        string[] notes =
        [
            "Theo ngày chấp thuận báo giá, giá chốt từng SKU/batch từ phiên bản báo giá được chấp thuận mới nhất.",
            "Tách rõ giảm do Chiến dịch khuyến mãi vs. Giảm theo HSD để đánh giá chi phí marketing tách biệt với đẩy hàng cận date.",
            "Hỗ trợ lọc riêng đơn Hoàn tất (Completed) hoặc toàn bộ báo giá đã duyệt để đối soát với doanh thu ghi nhận.",
        ];

        return new PromotionsReportResult(q.FromDate, q.ToDate, scope, summary, byProgram, Paginate(mapped, q.Page, q.PageSize), notes);
    }

    public async Task<LoyaltyReportResult> GetLoyaltyAsync(
        LoyaltyReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var (start, end) = PeriodBounds(q.FromDate, q.ToDate);
        var (groupSet, scope) = await ResolveGroupScopeAsync(null, actorUserId, actorRole, ct);
        var now = timeProvider.GetUtcNow();
        var overdueThreshold = now.AddDays(-3);

        HashSet<Guid>? scopedRetailers = null;
        if (groupSet != null)
        {
            scopedRetailers = (await db.RetailerSalesAssignmentRecords.AsNoTracking()
                .Where(a => a.IsActive && a.UnassignedAt == null && groupSet.Contains(a.SalesGroupId))
                .Select(a => a.RetailerId).Distinct().ToArrayAsync(ct)).ToHashSet();
        }

        var retailerTerm = q.Retailer?.Trim().ToLowerInvariant();
        var programTerm = q.Program?.Trim().ToLowerInvariant();

        // Redemptions in period
        var redemptionsRaw = await (
            from x in db.RewardRedemptionRecords.AsNoTracking()
            join r in db.RetailerRecords.AsNoTracking() on x.RetailerId equals r.Id
            join gift in db.LoyaltyRewardRecords.AsNoTracking() on x.LoyaltyRewardId equals gift.Id
            join p in db.LoyaltyProgramRecords.AsNoTracking() on gift.LoyaltyProgramId equals p.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            where x.CreatedAt >= start && x.CreatedAt < end
                && (!q.RegionId.HasValue || r.RegionId == q.RegionId.Value)
            select new
            {
                x.Id,
                x.CreatedAt,
                x.UpdatedAt,
                RetailerId = r.Id,
                RetailerCode = r.Code,
                RetailerName = r.Name,
                RegionName = reg != null ? reg.Name : "",
                ProgramCode = p.Code,
                ProgramName = p.Name,
                RewardCode = gift.Code,
                RewardName = gift.Name,
                x.Quantity,
                x.RedeemedPoints,
                x.Status,
                x.SalesOrderId,
                x.LoyaltyTransactionId,
                x.RedeemedByUserId,
                x.ApprovedByUserId,
                x.ApprovedAt,
                x.DeliveredByUserId,
                x.DeliveredAt,
                x.FulfilledAt,
                x.CancelledAt,
                x.Note
            }).ToArrayAsync(ct);

        if (scopedRetailers != null)
            redemptionsRaw = redemptionsRaw.Where(x => scopedRetailers.Contains(x.RetailerId)).ToArray();

        redemptionsRaw = redemptionsRaw
            .Where(x => string.IsNullOrWhiteSpace(retailerTerm) ||
                x.RetailerCode.ToLowerInvariant().Contains(retailerTerm) ||
                x.RetailerName.ToLowerInvariant().Contains(retailerTerm))
            .Where(x => string.IsNullOrWhiteSpace(programTerm) ||
                x.ProgramCode.ToLowerInvariant().Contains(programTerm) ||
                x.ProgramName.ToLowerInvariant().Contains(programTerm) ||
                x.RewardCode.ToLowerInvariant().Contains(programTerm) ||
                x.RewardName.ToLowerInvariant().Contains(programTerm))
            .ToArray();

        // Ledger transactions in period
        var ledgerRaw = await (
            from x in db.LoyaltyTransactionRecords.AsNoTracking()
            join card in db.RetailerLoyaltyRecords.AsNoTracking() on x.RetailerLoyaltyId equals card.Id
            join r in db.RetailerRecords.AsNoTracking() on card.RetailerId equals r.Id
            join p in db.LoyaltyProgramRecords.AsNoTracking() on x.LoyaltyProgramId equals p.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            where x.CreatedAt >= start && x.CreatedAt < end
                && (!q.RegionId.HasValue || r.RegionId == q.RegionId.Value)
            select new
            {
                x.Id,
                x.CreatedAt,
                RetailerId = r.Id,
                RetailerCode = r.Code,
                RetailerName = r.Name,
                RegionName = reg != null ? reg.Name : "",
                ProgramCode = p.Code,
                ProgramName = p.Name,
                x.TransactionType,
                x.Points,
                x.BalanceAfter,
                x.SalesOrderId,
                x.CreatedByUserId,
                x.Note
            }).ToArrayAsync(ct);

        if (scopedRetailers != null)
            ledgerRaw = ledgerRaw.Where(x => scopedRetailers.Contains(x.RetailerId)).ToArray();

        ledgerRaw = ledgerRaw
            .Where(x => string.IsNullOrWhiteSpace(retailerTerm) ||
                x.RetailerCode.ToLowerInvariant().Contains(retailerTerm) ||
                x.RetailerName.ToLowerInvariant().Contains(retailerTerm))
            .Where(x => string.IsNullOrWhiteSpace(programTerm) ||
                x.ProgramCode.ToLowerInvariant().Contains(programTerm) ||
                x.ProgramName.ToLowerInvariant().Contains(programTerm))
            .ToArray();

        // Card balances
        var cardRows = await (
            from card in db.RetailerLoyaltyRecords.AsNoTracking()
            join r in db.RetailerRecords.AsNoTracking() on card.RetailerId equals r.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            where !q.RegionId.HasValue || r.RegionId == q.RegionId.Value
            select new
            {
                card.Id,
                RetailerId = r.Id,
                RetailerCode = r.Code,
                RetailerName = r.Name,
                RegionName = reg != null ? reg.Name : "",
                card.CurrentPoints,
                card.HeldPoints,
                card.LifetimeEarnedPoints,
                card.LifetimeRedeemedPoints
            }).ToArrayAsync(ct);

        if (scopedRetailers != null)
            cardRows = cardRows.Where(x => scopedRetailers.Contains(x.RetailerId)).ToArray();

        var filteredCards = cardRows
            .Where(x => string.IsNullOrWhiteSpace(retailerTerm) ||
                x.RetailerCode.ToLowerInvariant().Contains(retailerTerm) ||
                x.RetailerName.ToLowerInvariant().Contains(retailerTerm))
            .ToArray();

        var moving = ledgerRaw.Where(x => x.TransactionType is not ("PendingEarn" or "PendingEarnCancelled" or "Pending")).ToArray();
        var periodEarned = moving.Where(x => x.Points > 0).Sum(x => (decimal)x.Points);
        var periodUsed = moving.Where(x => x.Points < 0).Sum(x => (decimal)(-x.Points));
        var periodExpired = ledgerRaw.Where(x => x.TransactionType == "Expired").Sum(x => (decimal)(-x.Points));

        var openRedStatuses = new HashSet<string> { "Pending", "Approved", "Delivered" };
        var summary = new LoyaltyReportSummary(
            redemptionsRaw.Length,
            redemptionsRaw.Count(x => openRedStatuses.Contains(x.Status)),
            redemptionsRaw.Count(x => x.Status == "Completed"),
            redemptionsRaw.Count(x => openRedStatuses.Contains(x.Status) && x.UpdatedAt < overdueThreshold),
            redemptionsRaw.Where(x => x.Status == "Completed").Sum(x => (decimal)x.RedeemedPoints),
            redemptionsRaw.Where(x => x.Status is "Completed" or "Delivered").Sum(x => (decimal)(x.Quantity ?? 0)),
            periodEarned,
            periodUsed,
            periodExpired,
            periodEarned - periodUsed,
            filteredCards.Sum(c => (decimal)c.CurrentPoints),
            filteredCards.Sum(c => (decimal)c.HeldPoints),
            filteredCards.Length);

        var orderIds = redemptionsRaw.Select(x => x.SalesOrderId).Concat(ledgerRaw.Select(x => x.SalesOrderId))
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var orderCodeById = orderIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.SalesOrderRecords.AsNoTracking().Where(o => orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderCode, ct);

        BusinessPagedList<LoyaltyRedemptionItem> redemptionsPage = EmptyPage<LoyaltyRedemptionItem>(q.Page, q.PageSize);
        BusinessPagedList<LoyaltyLedgerItem> ledgerPage = EmptyPage<LoyaltyLedgerItem>(q.Page, q.PageSize);
        BusinessPagedList<LoyaltyBalanceItem> balancesPage = EmptyPage<LoyaltyBalanceItem>(q.Page, q.PageSize);

        if (q.Tab == "redemptions")
        {
            var filtered = redemptionsRaw
                .Where(x => string.IsNullOrWhiteSpace(q.Status) || x.Status == q.Status)
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .ToArray();
            var userNames = await LoadUserNamesAsync(filtered.SelectMany(x =>
                new Guid?[] { x.RedeemedByUserId, x.ApprovedByUserId, x.DeliveredByUserId }), ct);
            var items = filtered.Select(x => new LoyaltyRedemptionItem(
                x.Id, x.CreatedAt, x.RetailerId, x.RetailerCode, x.RetailerName, x.RegionName,
                x.ProgramCode, x.ProgramName, x.RewardCode, x.RewardName, x.Quantity, x.RedeemedPoints,
                x.Status, x.SalesOrderId,
                x.SalesOrderId.HasValue ? orderCodeById.GetValueOrDefault(x.SalesOrderId.Value) : null,
                x.LoyaltyTransactionId,
                userNames.GetValueOrDefault(x.RedeemedByUserId),
                x.ApprovedByUserId,
                x.ApprovedByUserId.HasValue ? userNames.GetValueOrDefault(x.ApprovedByUserId.Value) : null,
                x.ApprovedAt,
                x.DeliveredByUserId.HasValue ? userNames.GetValueOrDefault(x.DeliveredByUserId.Value) : null,
                x.DeliveredAt, x.FulfilledAt, x.CancelledAt, x.Note)).ToArray();
            redemptionsPage = Paginate(items, q.Page, q.PageSize);
        }
        else if (q.Tab == "ledger")
        {
            var filtered = ledgerRaw
                .Where(x => string.IsNullOrWhiteSpace(q.Status) || x.TransactionType == q.Status)
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .ToArray();
            var userNames = await LoadUserNamesAsync(filtered.Select(x => x.CreatedByUserId), ct);
            var items = filtered.Select(x => new LoyaltyLedgerItem(
                x.Id, x.CreatedAt, x.RetailerId, x.RetailerCode, x.RetailerName, x.RegionName,
                x.ProgramCode, x.ProgramName, x.TransactionType, x.Points, x.BalanceAfter,
                x.SalesOrderId,
                x.SalesOrderId.HasValue ? orderCodeById.GetValueOrDefault(x.SalesOrderId.Value) : null,
                x.CreatedByUserId.HasValue ? userNames.GetValueOrDefault(x.CreatedByUserId.Value) : null,
                x.Note)).ToArray();
            ledgerPage = Paginate(items, q.Page, q.PageSize);
        }
        else if (q.Tab == "balances")
        {
            var rIds = filteredCards.Select(c => c.RetailerId).ToArray();
            var cardIds = filteredCards.Select(c => c.Id).ToArray();

            var groupNames = rIds.Length == 0
                ? new Dictionary<Guid, string>()
                : (await (
                    from a in db.RetailerSalesAssignmentRecords.AsNoTracking()
                    where a.IsActive && a.UnassignedAt == null && rIds.Contains(a.RetailerId)
                    join g in db.SalesGroupRecords.AsNoTracking() on a.SalesGroupId equals g.Id
                    select new { a.RetailerId, g.Name }
                ).ToArrayAsync(ct))
                .GroupBy(x => x.RetailerId)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var pendingCounts = rIds.Length == 0
                ? new Dictionary<Guid, int>()
                : await db.RewardRedemptionRecords.AsNoTracking()
                    .Where(x => rIds.Contains(x.RetailerId) && (x.Status == "Pending" || x.Status == "Approved" || x.Status == "Delivered"))
                    .GroupBy(x => x.RetailerId)
                    .Select(g => new { RetailerId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.RetailerId, x => x.Count, ct);

            var lastTx = cardIds.Length == 0
                ? new Dictionary<Guid, DateTimeOffset>()
                : await db.LoyaltyTransactionRecords.AsNoTracking()
                    .Where(t => cardIds.Contains(t.RetailerLoyaltyId))
                    .GroupBy(t => t.RetailerLoyaltyId)
                    .Select(g => new { CardId = g.Key, At = g.Max(t => t.CreatedAt) })
                    .ToDictionaryAsync(x => x.CardId, x => x.At, ct);

            var expiryByRetailer = rIds.Length == 0
                ? new Dictionary<Guid, DateTimeOffset?>()
                : (await (
                    from e in db.LoyaltyProgramRetailerRecords.AsNoTracking()
                    join p in db.LoyaltyProgramRecords.AsNoTracking() on e.LoyaltyProgramId equals p.Id
                    where rIds.Contains(e.RetailerId) && p.IsActive && p.PointsExpireAt != null && p.PointsExpireAt > now
                    select new { e.RetailerId, p.PointsExpireAt }
                ).ToArrayAsync(ct))
                .GroupBy(x => x.RetailerId)
                .ToDictionary(g => g.Key, g => g.Min(x => x.PointsExpireAt));

            var items = filteredCards
                .OrderByDescending(c => c.CurrentPoints).ThenByDescending(c => c.LifetimeEarnedPoints)
                .Select(c => new LoyaltyBalanceItem(
                    c.RetailerId, c.RetailerCode, c.RetailerName, c.RegionName,
                    groupNames.GetValueOrDefault(c.RetailerId, "—"),
                    c.CurrentPoints, c.HeldPoints, c.LifetimeEarnedPoints, c.LifetimeRedeemedPoints,
                    pendingCounts.GetValueOrDefault(c.RetailerId, 0),
                    lastTx.TryGetValue(c.Id, out var at) ? at : null,
                    expiryByRetailer.GetValueOrDefault(c.RetailerId)))
                .ToArray();
            balancesPage = Paginate(items, q.Page, q.PageSize);
        }

        string[] notes =
        [
            "Gộp 3 góc nhìn Loyalty trong một báo cáo: Yêu cầu đổi quà (Redemptions), Sổ biến động điểm (Ledger), và Bảng số dư / điểm sắp hết hạn theo Retailer.",
            "RedemptionHeld trừ điểm khả dụng ngay khi gửi yêu cầu; RedemptionCompleted có biến động = 0 vì điểm đã được giữ trước đó.",
        ];

        return new LoyaltyReportResult(
            q.FromDate, q.ToDate, q.Tab, scope, summary, redemptionsPage, ledgerPage, balancesPage, notes);
    }

    public async Task<ClaimsReportResult> GetClaimsAsync(
        ClaimsReportQuery q, Guid actorUserId, string actorRole, CancellationToken ct)
    {
        var (start, end) = PeriodBounds(q.FromDate, q.ToDate);
        var (groupSet, scope) = await ResolveGroupScopeAsync(null, actorUserId, actorRole, ct);

        var raw = await (
            from d in db.ReceiptDiscrepancyRecords.AsNoTracking()
            join o in db.SalesOrderRecords.AsNoTracking() on d.SalesOrderId equals o.Id
            join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
            join g in db.SalesGroupRecords.AsNoTracking() on o.SalesGroupId equals g.Id
            join reg in db.RegionRecords.AsNoTracking() on r.RegionId equals reg.Id into regRows
            from reg in regRows.DefaultIfEmpty()
            join sales in db.UserAccountRecords.AsNoTracking() on o.CreatedByUserId equals sales.Id into sRows
            from sales in sRows.DefaultIfEmpty()
            where d.ReportedAt >= start && d.ReportedAt < end
                && (!q.RegionId.HasValue || r.RegionId == q.RegionId.Value)
            select new
            {
                d.Id,
                d.ReportedAt,
                OrderId = o.Id,
                o.OrderCode,
                o.SalesGroupId,
                RetailerId = r.Id,
                RetailerCode = o.RetailerCodeSnapshot,
                RetailerName = o.RetailerNameSnapshot,
                RegionName = reg != null ? reg.Name : "",
                SalesGroupName = g.Name,
                SalesUserName = sales != null ? sales.FullName : "",
                d.RequestType,
                d.ApprovedSettlementMethod,
                d.SettlementMethod,
                d.ResolutionType,
                d.Status,
                d.RequestedQuantity,
                d.ApprovedQuantity,
                d.HandledQuantity,
                d.RequiresCollection,
                d.PhysicalHandlingCompleted,
                d.FinancialAdjustmentApplied,
                d.DebtOffsetAmount,
                d.RefundAmount,
                d.RefundId,
                d.ReviewedByUserId,
                d.ReviewedAt,
                d.ResolvedAt,
                d.Note
            }).ToArrayAsync(ct);

        if (groupSet != null)
            raw = raw.Where(x => groupSet.Contains(x.SalesGroupId)).ToArray();

        var retailerTerm = q.Retailer?.Trim().ToLowerInvariant();
        var searchTerm = q.Search?.Trim().ToLowerInvariant();

        var claimIds = raw.Select(x => x.Id).ToArray();
        var orderIds = raw.Select(x => x.OrderId).Distinct().ToArray();

        // Attribute warehouse via linked discrepancy items -> Shipment -> Warehouse, fallback first shipment of order
        var itemWh = claimIds.Length == 0
            ? new Dictionary<Guid, (string Code, string Name)>()
            : (await (
                from di in db.ReceiptDiscrepancyItemRecords.AsNoTracking()
                where claimIds.Contains(di.ReceiptDiscrepancyId) && di.ShipmentId != null
                join sh in db.ShipmentRecords.AsNoTracking() on di.ShipmentId equals sh.Id
                join wh in db.WarehouseRecords.AsNoTracking() on sh.WarehouseId equals wh.Id
                select new { di.ReceiptDiscrepancyId, wh.Code, wh.Name }
            ).ToArrayAsync(ct))
            .GroupBy(x => x.ReceiptDiscrepancyId)
            .ToDictionary(g => g.Key, g => (g.First().Code, g.First().Name));

        var orderWh = orderIds.Length == 0
            ? new Dictionary<Guid, (string Code, string Name)>()
            : (await (
                from sh in db.ShipmentRecords.AsNoTracking()
                where orderIds.Contains(sh.SalesOrderId)
                join wh in db.WarehouseRecords.AsNoTracking() on sh.WarehouseId equals wh.Id
                select new { sh.SalesOrderId, wh.Code, wh.Name }
            ).ToArrayAsync(ct))
            .GroupBy(x => x.SalesOrderId)
            .ToDictionary(g => g.Key, g => (g.First().Code, g.First().Name));

        var returnDocs = claimIds.Length == 0
            ? new Dictionary<Guid, (string Code, string Status)>()
            : (await db.ReturnHandlingDocumentRecords.AsNoTracking()
                    .Where(rd => claimIds.Contains(rd.ReceiptDiscrepancyId))
                    .Select(rd => new { rd.ReceiptDiscrepancyId, rd.Code, rd.Status })
                    .ToArrayAsync(ct))
                .GroupBy(x => x.ReceiptDiscrepancyId)
                .ToDictionary(g => g.Key, g => (g.First().Code, g.First().Status));

        var refundIds = raw.Where(x => x.RefundId.HasValue).Select(x => x.RefundId!.Value).Distinct().ToArray();
        var refundStatusById = refundIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.ManualRefundRecords.AsNoTracking()
                .Where(mr => refundIds.Contains(mr.Id))
                .ToDictionaryAsync(mr => mr.Id, mr => mr.Status, ct);

        var reviewerNames = await LoadUserNamesAsync(raw.Select(x => x.ReviewedByUserId), ct);

        var mapped = raw
            .Select(x =>
            {
                var claimCode = $"KN-{x.Id.ToString("N")[..8].ToUpperInvariant()}";
                var wh = itemWh.TryGetValue(x.Id, out var w1) ? w1
                    : orderWh.TryGetValue(x.OrderId, out var w2) ? w2 : ("—", "Không xác định");
                returnDocs.TryGetValue(x.Id, out var rd);
                string? refundStatus = x.RefundId.HasValue ? refundStatusById.GetValueOrDefault(x.RefundId.Value) : null;
                string? refundCode = x.RefundId.HasValue ? $"HT-{x.RefundId.Value.ToString("N")[..8].ToUpperInvariant()}" : null;
                decimal? resHours = x.ResolvedAt.HasValue
                    ? Math.Round((decimal)(x.ResolvedAt.Value - x.ReportedAt).TotalHours, 1)
                    : null;

                return new ClaimReportItem(
                    x.Id, claimCode, x.ReportedAt, x.OrderId, x.OrderCode, x.RetailerId,
                    x.RetailerCode, x.RetailerName, x.RegionName, x.SalesGroupName, x.SalesUserName,
                    wh.Item1, wh.Item2, x.RequestType,
                    x.ApprovedSettlementMethod ?? x.SettlementMethod,
                    x.ResolutionType ?? x.RequestType,
                    x.Status,
                    x.RequestedQuantity, x.ApprovedQuantity, x.HandledQuantity,
                    x.RequiresCollection, x.PhysicalHandlingCompleted, x.FinancialAdjustmentApplied,
                    x.DebtOffsetAmount, x.RefundAmount, x.RefundId, refundCode, refundStatus,
                    rd.Code, rd.Status,
                    x.ReviewedByUserId.HasValue ? reviewerNames.GetValueOrDefault(x.ReviewedByUserId.Value) : null,
                    x.ReviewedAt, x.ResolvedAt, resHours, x.Note);
            })
            .Where(x => string.IsNullOrWhiteSpace(retailerTerm) ||
                x.RetailerCode.ToLowerInvariant().Contains(retailerTerm) ||
                x.RetailerName.ToLowerInvariant().Contains(retailerTerm))
            .Where(x => string.IsNullOrWhiteSpace(searchTerm) ||
                x.ClaimCode.ToLowerInvariant().Contains(searchTerm) ||
                x.OrderCode.ToLowerInvariant().Contains(searchTerm) ||
                x.WarehouseCode.ToLowerInvariant().Contains(searchTerm))
            .Where(x => string.IsNullOrWhiteSpace(q.RequestType) || x.RequestType == q.RequestType)
            .Where(x => string.IsNullOrWhiteSpace(q.SettlementMethod) || x.SettlementMethod == q.SettlementMethod)
            .Where(x => string.IsNullOrWhiteSpace(q.Status) || x.Status == q.Status)
            .OrderByDescending(x => x.ReportedAt)
            .ToArray();

        var resolvedHours = mapped.Where(x => x.ResolutionHours.HasValue).Select(x => x.ResolutionHours!.Value).ToArray();
        var summary = new ClaimsSummary(
            mapped.Length,
            mapped.Count(x => x.ResolvedAt == null && x.Status is not ("Rejected" or "Cancelled" or "Closed" or "Resolved")),
            mapped.Count(x => x.ResolvedAt != null || x.Status is "Resolved" or "Closed"),
            mapped.Count(x => x.RequiresCollection && !x.PhysicalHandlingCompleted && x.Status is not ("Rejected" or "Cancelled")),
            resolvedHours.Length == 0 ? 0m : Math.Round(resolvedHours.Average(), 1),
            mapped.Sum(x => x.RequestedQuantity),
            mapped.Sum(x => x.ApprovedQuantity ?? 0m),
            mapped.Sum(x => x.DebtOffsetAmount),
            mapped.Sum(x => x.RefundAmount),
            mapped.Where(x => x.RefundStatus == "Refunded").Sum(x => x.RefundAmount));

        var byWarehouse = mapped
            .GroupBy(x => new { x.WarehouseCode, x.WarehouseName })
            .Select(g => new ClaimsWarehouseBreakdown(
                g.Key.WarehouseCode, g.Key.WarehouseName,
                g.Count(),
                g.Count(x => x.ResolvedAt == null && x.Status is not ("Rejected" or "Cancelled" or "Closed" or "Resolved")),
                g.Sum(x => x.ApprovedQuantity ?? x.RequestedQuantity),
                g.Sum(x => x.DebtOffsetAmount + x.RefundAmount)))
            .OrderByDescending(w => w.ClaimCount).ThenByDescending(w => w.FinancialImpactAmount)
            .ToArray();

        string[] notes =
        [
            "Theo dõi toàn bộ quy trình hậu mãi: tiếp nhận (Complaint/Return/DamagedCollection), thu hồi hàng thực tế và cấn trừ công nợ / hoàn tiền.",
            "Phân bổ khiếu nại theo kho giao hàng giúp phát hiện sớm điểm nghẽn đóng gói hoặc vận chuyển.",
        ];

        return new ClaimsReportResult(q.FromDate, q.ToDate, scope, summary, byWarehouse, Paginate(mapped, q.Page, q.PageSize), notes);
    }
}
