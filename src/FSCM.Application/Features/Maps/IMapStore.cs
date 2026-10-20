using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Maps;

public interface IMapStore
{
    Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken);
    Task<bool> RetailerExistsAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<MapLocation?> FindLocationAsync(string entityType, Guid entityId, bool tracking, CancellationToken cancellationToken);
    Task<MapDistanceCache?> FindActiveDistanceAsync(Guid warehouseId, Guid retailerId, bool tracking, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MapDistanceCache>> ListDistancesAsync(Guid? warehouseId, Guid? retailerId, CancellationToken cancellationToken);
    Task<TransitTimeRule?> FindTransitRuleAsync(Guid warehouseId, int distanceMeters, CancellationToken cancellationToken);
    void AddLocation(MapLocation location);
    void AddDistance(MapDistanceCache distance);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
