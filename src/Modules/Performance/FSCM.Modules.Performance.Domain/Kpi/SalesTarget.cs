using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Performance.Domain.Kpi;

/// <summary>Monthly KPI target of one Sales with the weights of the three indicators (must sum to 1).</summary>
public sealed class SalesTarget : AggregateRoot<string>
{
    private SalesTarget() { }
    public SalesTarget(string period, string salesUserId, decimal revenueTarget, int newStoreTarget, int orderTarget, decimal wRevenue = 0.6m, decimal wNewStores = 0.2m, decimal wOrders = 0.2m)
    {
        Id = Key(period, salesUserId); Period = period; SalesUserId = salesUserId; Set(revenueTarget, newStoreTarget, orderTarget, wRevenue, wNewStores, wOrders);
    }
    public static string Key(string period, string userId) => $"{period}:{userId}";
    public string Period { get; private set; } = "";
    public string SalesUserId { get; private set; } = "";
    public decimal RevenueTarget { get; private set; }
    public int NewStoreTarget { get; private set; }
    public int OrderTarget { get; private set; }
    public decimal WeightRevenue { get; private set; }
    public decimal WeightNewStores { get; private set; }
    public decimal WeightOrders { get; private set; }
    public void Set(decimal revenueTarget, int newStoreTarget, int orderTarget, decimal wRevenue, decimal wNewStores, decimal wOrders)
    {
        Guard.Against(revenueTarget < 0 || newStoreTarget < 0 || orderTarget < 0, "kpi.target", "Chỉ tiêu không âm.");
        Guard.Against(wRevenue < 0 || wNewStores < 0 || wOrders < 0 || Math.Abs(wRevenue + wNewStores + wOrders - 1m) > 0.001m, "kpi.weights", "Ba trọng số KPI không âm và có tổng bằng 1.");
        RevenueTarget = revenueTarget; NewStoreTarget = newStoreTarget; OrderTarget = orderTarget; WeightRevenue = wRevenue; WeightNewStores = wNewStores; WeightOrders = wOrders;
    }
}
