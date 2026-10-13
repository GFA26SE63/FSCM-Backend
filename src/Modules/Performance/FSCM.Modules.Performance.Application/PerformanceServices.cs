using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Identity.Contracts;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Partners.Contracts;
using FSCM.Modules.Performance.Contracts;
using FSCM.Modules.Performance.Domain;
using FSCM.Modules.Performance.Domain.Facts;
using FSCM.Modules.Performance.Domain.Kpi;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Performance.Application;

public sealed record SalesKpiDto(string SalesUserId, string FullName, string? GroupCode, int Orders, decimal RevenueBeforePromotion, decimal Revenue,
    int NewStores, decimal RevenueTarget, int NewStoreTarget, int OrderTarget, decimal RevenueAttainment, decimal Score);
public sealed record TargetInput(string Period, string SalesUserId, decimal RevenueTarget, int NewStoreTarget, int OrderTarget, decimal WeightRevenue = 0.6m, decimal WeightNewStores = 0.2m, decimal WeightOrders = 0.2m);
public sealed record SalesRankingDto(int Rank, string SalesUserId, string FullName, string? GroupCode, int Orders, decimal Revenue);
public sealed record ProductRankingDto(int Rank, string Sku, int Cases, decimal Revenue);
public sealed record DailyRevenueDto(DateOnly Date, decimal Revenue, int Orders);
public sealed record DashboardDto(string Period, int Orders, decimal Gross, decimal Discount, decimal Revenue, int NewStores,
    IReadOnlyList<SalesRankingDto> TopSales, IReadOnlyList<ProductRankingDto> TopProducts, IReadOnlyList<DailyRevenueDto> Daily);

/// <summary>Reads the fact tables built from Ordering events. Sales see only their own KPI; ranking and top products are visible to Sales for motivation.</summary>
public sealed class PerformanceQueryService(IFactRepository facts, IIdentityModule identity, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<SalesKpiDto>> SalesKpiAsync(string? period, CancellationToken ct)
    {
        var range = Period.Parse(period, clock.Today); var key = Period.MonthKey(range.From);
        var orders = await facts.OrdersAsync(range, ct); var stores = await facts.NewStoresAsync(range, ct);
        var targets = (await facts.TargetsAsync(key, ct)).ToDictionary(t => t.SalesUserId);
        var revenueByUser = orders.Where(o => o.ClosedBySales).GroupBy(o => o.SalesUserId!).ToDictionary(g => g.Key);
        var ids = revenueByUser.Keys.Concat(targets.Keys).Concat(stores.Select(s => s.SalesUserId)).Distinct().Where(id => user.Role != Role.Sales || id == user.UserId).ToList();
        var list = new List<SalesKpiDto>();
        foreach (var id in ids)
        {
            var u = await identity.GetUserAsync(id, ct);
            var mine = revenueByUser.TryGetValue(id, out var g) ? g.ToList() : new List<SalesOrderFact>();
            var newStores = stores.Count(s => s.SalesUserId == id);
            var t = targets.GetValueOrDefault(id);
            var score = t is null ? null : KpiCalculator.Score(t, mine.Sum(o => o.Net), newStores, mine.Count);
            list.Add(new SalesKpiDto(id, u?.FullName ?? id, u?.SalesGroupCode, mine.Count, mine.Sum(o => o.Gross), mine.Sum(o => o.Net), newStores,
                t?.RevenueTarget ?? 0, t?.NewStoreTarget ?? 0, t?.OrderTarget ?? 0, score?.RevenueAttainment ?? 0, score?.Score ?? 0));
        }
        return list.OrderByDescending(k => k.Revenue).ToList();
    }

    public async Task<IReadOnlyList<SalesRankingDto>> SalesRankingAsync(string? period, CancellationToken ct)
    {
        var range = Period.Parse(period, clock.Today);
        var rows = Rankings.Sales(await facts.OrdersAsync(range, ct), range);
        var list = new List<SalesRankingDto>();
        foreach (var r in rows)
        {
            var u = await identity.GetUserAsync(r.SalesUserId, ct);
            list.Add(new SalesRankingDto(r.Rank, r.SalesUserId, u?.FullName ?? r.SalesUserId, u?.SalesGroupCode, r.Orders, r.Revenue));
        }
        return list;
    }

    public async Task<IReadOnlyList<ProductRankingDto>> ProductRankingAsync(string? period, int top, CancellationToken ct)
    {
        var range = Period.Parse(period, clock.Today);
        return Rankings.Products(await facts.OrdersAsync(range, ct), range, top).Select(r => new ProductRankingDto(r.Rank, r.Sku, r.Cases, r.Revenue)).ToList();
    }

    public async Task<DashboardDto> DashboardAsync(string? period, CancellationToken ct)
    {
        var range = Period.Parse(period, clock.Today);
        var orders = await facts.OrdersAsync(range, ct);
        var daily = orders.GroupBy(o => o.CompletedOn).OrderBy(g => g.Key).Select(g => new DailyRevenueDto(g.Key, g.Sum(o => o.Net), g.Count())).ToList();
        return new DashboardDto(Period.MonthKey(range.From), orders.Count, orders.Sum(o => o.Gross), orders.Sum(o => o.Discount), orders.Sum(o => o.Net), (await facts.NewStoresAsync(range, ct)).Count,
            (await SalesRankingAsync(period, ct)).Take(5).ToList(), await ProductRankingAsync(period, 5, ct), daily);
    }
}

public sealed class TargetService(IFactRepository facts, IPerformanceUnitOfWork uow)
{
    public async Task SetAsync(TargetInput x, CancellationToken ct)
    {
        var t = await facts.TargetAsync(x.Period, x.SalesUserId, ct);
        if (t is null) facts.Add(new SalesTarget(x.Period, x.SalesUserId, x.RevenueTarget, x.NewStoreTarget, x.OrderTarget, x.WeightRevenue, x.WeightNewStores, x.WeightOrders));
        else t.Set(x.RevenueTarget, x.NewStoreTarget, x.OrderTarget, x.WeightRevenue, x.WeightNewStores, x.WeightOrders);
        await uow.SaveChangesAsync(ct);
    }
}

public sealed class PerformanceModule(IFactRepository facts, IClock clock) : IPerformanceModule
{
    public async Task<decimal> SalesRevenueAsync(string salesUserId, string? period, CancellationToken ct = default)
    {
        var range = Period.Parse(period, clock.Today);
        return Rankings.Sales(await facts.OrdersAsync(range, ct), range).FirstOrDefault(r => r.SalesUserId == salesUserId)?.Revenue ?? 0;
    }
}

/// <summary>Performance ← OrderCompleted. Idempotent by order code.</summary>
public sealed class OrderCompletedHandler(IFactRepository facts, IPerformanceUnitOfWork uow) : IIntegrationEventHandler<OrderCompleted>
{
    public async Task HandleAsync(OrderCompleted e, CancellationToken ct = default)
    {
        if (await facts.OrderExistsAsync(e.OrderCode, ct)) return;
        facts.Add(new SalesOrderFact(e.OrderCode, e.SalesUserId, e.RetailerCode, e.PaymentMethod, e.CompletedOn, e.GrossAmount, e.DiscountAmount, e.NetAmount,
            e.Lines.Select(l => new FactLine(l.Sku, l.Quantity, l.GrossAmount, l.DiscountAmount, l.NetAmount))));
        await uow.SaveChangesAsync(ct);
    }
}

public sealed class RetailerRegisteredHandler(IFactRepository facts, IPerformanceUnitOfWork uow, IClock clock) : IIntegrationEventHandler<RetailerRegistered>
{
    public async Task HandleAsync(RetailerRegistered e, CancellationToken ct = default)
    {
        if (await facts.NewStoreExistsAsync(e.RetailerCode, ct)) return;
        facts.Add(new NewStoreFact(e.RetailerCode, e.DeclaredByUserId, clock.Today));
        await uow.SaveChangesAsync(ct);
    }
}

public static class PerformanceApplication
{
    public static IServiceCollection AddPerformanceApplication(this IServiceCollection s) => s
        .AddScoped<IPerformanceModule, PerformanceModule>()
        .AddScoped<PerformanceQueryService>().AddScoped<TargetService>()
        .AddScoped<IIntegrationEventHandler<OrderCompleted>, OrderCompletedHandler>()
        .AddScoped<IIntegrationEventHandler<RetailerRegistered>, RetailerRegisteredHandler>();
}
