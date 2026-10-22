using FSCM.Application.Features.Maps;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Maps;

public sealed class MapStore(AppDbContext dbContext) : IMapStore
{
    public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.AsNoTracking().AnyAsync(item => item.Id == warehouseId, cancellationToken);

    public Task<bool> RetailerExistsAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.AsNoTracking().AnyAsync(item => item.Id == retailerId, cancellationToken);

    public Task<MapLocation?> FindLocationAsync(
        string entityType, Guid entityId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? dbContext.MapLocationRecords.AsQueryable() : dbContext.MapLocationRecords.AsNoTracking();
        return query.SingleOrDefaultAsync(item => entityType == "warehouse"
            ? item.WarehouseId == entityId
            : item.RetailerId == entityId, cancellationToken);
    }

    public Task<MapDistanceCache?> FindActiveDistanceAsync(
        Guid warehouseId, Guid retailerId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? dbContext.MapDistanceCacheRecords.AsQueryable() : dbContext.MapDistanceCacheRecords.AsNoTracking();
        return query.SingleOrDefaultAsync(item => item.WarehouseId == warehouseId &&
            item.RetailerId == retailerId && item.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyCollection<MapDistanceCache>> ListDistancesAsync(
        Guid? warehouseId, Guid? retailerId, CancellationToken cancellationToken)
    {
        var query = dbContext.MapDistanceCacheRecords.AsNoTracking().Where(item => item.IsActive);
        if (warehouseId.HasValue) query = query.Where(item => item.WarehouseId == warehouseId.Value);
        if (retailerId.HasValue) query = query.Where(item => item.RetailerId == retailerId.Value);
        return await query.OrderBy(item => item.WarehouseId).ThenBy(item => item.RetailerId)
            .ToArrayAsync(cancellationToken);
    }

    public void AddLocation(MapLocation location) => dbContext.MapLocationRecords.Add(location);
    public void AddDistance(MapDistanceCache distance) => dbContext.MapDistanceCacheRecords.Add(distance);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
