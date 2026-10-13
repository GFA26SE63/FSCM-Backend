namespace FSCM.Modules.Performance.Contracts;

public interface IPerformanceModule
{
    /// <summary>Revenue after promotion of the orders closed by this Sales in a period ("yyyy-MM", "yyyy-Qn" or "yyyy"; null = current month).</summary>
    Task<decimal> SalesRevenueAsync(string salesUserId, string? period, CancellationToken ct = default);
}
