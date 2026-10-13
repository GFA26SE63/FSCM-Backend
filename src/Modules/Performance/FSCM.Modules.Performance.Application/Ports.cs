using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Performance.Domain;
using FSCM.Modules.Performance.Domain.Facts;
using FSCM.Modules.Performance.Domain.Kpi;

namespace FSCM.Modules.Performance.Application;

public interface IPerformanceUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IFactRepository
{
    Task<bool> OrderExistsAsync(string orderCode, CancellationToken ct);
    void Add(SalesOrderFact f);
    Task<IReadOnlyList<SalesOrderFact>> OrdersAsync(DateRange range, CancellationToken ct);
    Task<bool> NewStoreExistsAsync(string retailerCode, CancellationToken ct);
    void Add(NewStoreFact f);
    Task<IReadOnlyList<NewStoreFact>> NewStoresAsync(DateRange range, CancellationToken ct);
    Task<SalesTarget?> TargetAsync(string period, string salesUserId, CancellationToken ct);
    Task<IReadOnlyList<SalesTarget>> TargetsAsync(string period, CancellationToken ct);
    void Add(SalesTarget t);
}
