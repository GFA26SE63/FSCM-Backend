namespace FSCM.Application.Features.Dashboard;

public sealed record DashboardQuery(DateOnly? FromDate = null, DateOnly? ToDate = null,
    Guid? RegionId = null, Guid? WarehouseId = null, Guid? SalesGroupId = null);
public sealed record DashboardOption(Guid Id, string Code, string Name, Guid? RegionId = null);
public sealed record DashboardScope(bool IsGlobal, bool HasAssignments, string Description,
    IReadOnlyCollection<DashboardOption> Regions, IReadOnlyCollection<DashboardOption> Warehouses,
    IReadOnlyCollection<DashboardOption> SalesGroups);
public sealed record DashboardCard(string Key, string Label, decimal Value, string Unit, string Note, string Tone = "neutral", string? Href = null);
public sealed record DashboardStatus(string Status, int Count);
public sealed record DashboardDay(DateOnly Date, decimal RecognizedRevenue, decimal CollectedAmount);
public sealed record DashboardProduct(Guid Id, string Sku, string Name, decimal Quantity, decimal Revenue);
public sealed record DashboardSalesPerson(Guid Id, string Name, Guid GroupId, string GroupCode,
    int CompletedOrders, int RetailersServed, decimal Revenue, decimal? KpiCompletion = null);
public sealed record DashboardWarehouse(Guid Id, string Code, string Name, decimal OnHand,
    decimal Reserved, decimal Locked, decimal Available, decimal NearExpiry, decimal Expired);
public sealed record DashboardExpiry(Guid Id, string BatchCode, string WarehouseCode, string Sku,
    DateOnly ExpirationDate, decimal OnHand, decimal Available, string Status);
public sealed record DashboardWorkItem(string Kind, Guid Id, string Code, string Title, string Status,
    string Reason, DateTimeOffset Since, int Priority, string? Href, decimal? Amount = null);
public sealed record DashboardCustomer(Guid Id, string Code, string Name, string Status,
    int PeriodOrders, DateTimeOffset? LastOrderAt, int ExpectedFrequencyPerMonth, decimal Outstanding, decimal Overdue);
public sealed record DashboardHealth(DateTimeOffset? ExpiryLastRunAt, string? ExpiryStatus,
    int PendingEvents, int FailedEvents, int AbandonedAllocations);
public sealed record DashboardResult(string Role, DateTimeOffset AsOf, DateOnly FromDate, DateOnly ToDate,
    DashboardScope Scope, IReadOnlyCollection<DashboardCard> Cards, IReadOnlyCollection<DashboardStatus> OrdersByStatus,
    IReadOnlyCollection<DashboardDay> RevenueByDay, IReadOnlyCollection<DashboardProduct> TopProducts,
    IReadOnlyCollection<DashboardSalesPerson> SalesTeam, IReadOnlyCollection<DashboardWarehouse> Inventory,
    IReadOnlyCollection<DashboardExpiry> ExpiryRisks, IReadOnlyCollection<DashboardWorkItem> WorkItems,
    IReadOnlyCollection<DashboardCustomer> Customers, DashboardHealth? Health, decimal? UnattributedRevenue,
    IReadOnlyCollection<string> Notes);

public interface IDashboardStore
{
    Task<DashboardResult> ReadAsync(Guid userId, string role, DashboardQuery query, DateTimeOffset now, CancellationToken ct);
}
