using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Contracts;
using FSCM.Modules.Finance.Domain.Receivables;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Partners.Contracts;
using FSCM.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Finance.Application;

/// <summary>Màn "Công nợ": Operator collects, Sales sees own retailers, Retailer sees own debts.</summary>
public sealed class ReceivableService(IReceivableRepository repo, IFinanceUnitOfWork uow, IPartnersModule partners, ICurrentUser user, IClock clock)
{
    public async Task<PagedResult<ReceivableDto>> ListAsync(PageRequest page, string? status, CancellationToken ct)
    {
        var today = clock.Today;
        var rows = await repo.SearchAsync(user.Role == Role.Sales ? user.UserId : null, user.Role == Role.Retailer ? user.RetailerCode : null, ct);
        var wanted = Enum.TryParse<ReceivableStatus>(status, true, out var s) ? (ReceivableStatus?)s : null;
        var filtered = rows.Where(r => wanted is null || r.EffectiveStatus(today) == wanted)
            .Where(r => string.IsNullOrWhiteSpace(page.Search) || r.Id.Contains(page.Search, StringComparison.OrdinalIgnoreCase) || r.RetailerCode.Contains(page.Search))
            .OrderBy(r => r.DueDate).ToList();
        var names = new Dictionary<string, string>();
        foreach (var code in filtered.Select(r => r.RetailerCode).Distinct())
            names[code] = (await partners.GetRetailerAsync(code, ct))?.Name ?? code;
        return filtered.Select(r => FinanceModule.Map(r, names[r.RetailerCode], today)).Page(page);
    }

    /// <summary>Single full collection. Raises CreditOrderPaid → Loyalty Pending → Earned, Ordering → Completed.</summary>
    public async Task CollectAsync(string orderCode, CancellationToken ct)
    {
        var r = await repo.GetAsync(orderCode, ct) ?? throw new BusinessRuleException("receivable.not_found", $"Không có công nợ của đơn {orderCode}.");
        r.Collect(user.FullName, clock.Today);
        uow.Enqueue(new CreditOrderPaid(r.Id, r.RetailerCode, r.SalesUserId, r.Amount, clock.Today));
        await uow.SaveChangesAsync(ct);
    }
}

public sealed class FinanceModule(IReceivableRepository repo, IPartnersModule partners, IClock clock) : IFinanceModule
{
    public async Task<ReceivableDto?> GetReceivableAsync(string orderCode, CancellationToken ct = default) =>
        await repo.GetAsync(orderCode, ct) is { } r ? Map(r, (await partners.GetRetailerAsync(r.RetailerCode, ct))?.Name ?? r.RetailerCode, clock.Today) : null;

    internal static ReceivableDto Map(Receivable r, string retailerName, DateOnly today) =>
        new(r.Id, r.RetailerCode, retailerName, r.SalesUserId, r.TermDays, r.DeliveredOn, r.DueDate, r.Amount, r.EffectiveStatus(today).ToString(), r.PaidOn, r.DaysOverdue(today));
}

/// <summary>Finance ← OrderDelivered: only credit orders create a receivable (COD / prepaid are settled at delivery).</summary>
public sealed class OrderDeliveredHandler(IReceivableRepository repo, IFinanceUnitOfWork uow) : IIntegrationEventHandler<OrderDelivered>
{
    public async Task HandleAsync(OrderDelivered e, CancellationToken ct = default)
    {
        if (e.PaymentMethod != "Credit" || e.CreditTermDays == 0) return;
        if (await repo.GetAsync(e.OrderCode, ct) is not null) return;              // idempotent: the event may be delivered twice
        repo.Add(new Receivable(e.OrderCode, e.RetailerCode, e.SalesUserId, e.CreditTermDays, e.DeliveredOn, e.NetAmount));
        await uow.SaveChangesAsync(ct);
    }
}

/// <summary>Daily 08:00: flag receivables past their due date and raise ReceivableOverdue once per receivable.</summary>
public sealed class ReceivableOverdueJob(IReceivableRepository repo, IFinanceUnitOfWork uow, IClock clock) : IScheduledJob
{
    public string Name => "receivable-overdue";
    public TimeOnly RunAt => new(8, 0);
    public async Task RunAsync(CancellationToken ct)
    {
        var today = clock.Today;
        foreach (var r in await repo.PastDueAsync(today, ct))
            if (r.MarkOverdue(today)) uow.Enqueue(new ReceivableOverdue(r.Id, r.RetailerCode, r.SalesUserId, r.Amount, r.DueDate, r.DaysOverdue(today)));
        await uow.SaveChangesAsync(ct);
    }
}

public static class FinanceApplication
{
    public static IServiceCollection AddFinanceApplication(this IServiceCollection s) => s
        .AddScoped<IFinanceModule, FinanceModule>()
        .AddScoped<ReceivableService>()
        .AddScoped<IIntegrationEventHandler<OrderDelivered>, OrderDeliveredHandler>()
        .AddScoped<IScheduledJob, ReceivableOverdueJob>();
}
