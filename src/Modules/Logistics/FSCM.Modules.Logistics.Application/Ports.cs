using FSCM.Modules.Logistics.Domain.Distances;
using FSCM.Modules.Logistics.Domain.Fleet;
using FSCM.Modules.Logistics.Domain.Transit;

namespace FSCM.Modules.Logistics.Application;

public interface ITransitRuleRepository
{
    Task<IReadOnlyList<TransitRule>> InForceAsync(DateOnly onDate, CancellationToken ct);
    Task ReplaceTableAsync(IEnumerable<TransitRule> rules, CancellationToken ct);
    Task<DistanceLimits> LimitsAsync(CancellationToken ct);
    Task SaveLimitsAsync(DistanceLimits limits, CancellationToken ct);
}
public interface ILogisticsUnitOfWork : FSCM.BuildingBlocks.Application.IUnitOfWork { }
public interface IDistanceRepository { Task<DistanceEntry?> FindAsync(string fromWarehouse, string toPoint, CancellationToken ct); }
public interface IFleetRepository
{
    Task<Vehicle?> VehicleAsync(string plate, CancellationToken ct);
    Task<Driver?> DriverAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Vehicle>> VehiclesAsync(CancellationToken ct);
    Task<IReadOnlyList<Driver>> DriversAsync(CancellationToken ct);
    void Add(Vehicle v); void Add(Driver d);
    Task<int> DriverCountAsync(CancellationToken ct);
}
