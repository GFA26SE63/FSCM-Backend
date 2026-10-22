using FSCM.Application.Features.DeliveryEta;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.DeliveryEta;

public sealed class DeliveryEtaStore(AppDbContext dbContext) : IDeliveryEtaStore
{
    private const string ScheduleConfigurationKey = "eta.schedule";

    public Task<SystemConfiguration?> FindScheduleConfigurationAsync(CancellationToken cancellationToken) =>
        dbContext.SystemConfigurationRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ConfigKey == ScheduleConfigurationKey && item.IsActive, cancellationToken);

    public Task<MapDistanceCache?> FindDistanceAsync(
        Guid warehouseId, Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.MapDistanceCacheRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WarehouseId == warehouseId && item.RetailerId == retailerId && item.IsActive,
                cancellationToken);

    public Task<MapLocation?> FindLocationAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken) =>
        entityType == "warehouse"
            ? dbContext.MapLocationRecords.AsNoTracking()
                .SingleOrDefaultAsync(item => item.WarehouseId == entityId, cancellationToken)
            : dbContext.MapLocationRecords.AsNoTracking()
                .SingleOrDefaultAsync(item => item.RetailerId == entityId, cancellationToken);
}
