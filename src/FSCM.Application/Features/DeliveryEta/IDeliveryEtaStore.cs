using FSCM.Domain.Entities;

namespace FSCM.Application.Features.DeliveryEta;

public sealed record EtaScheduleView(EtaSchedule Schedule, int Version);

public interface IDeliveryEtaStore
{
    /// <summary>The active <c>eta.schedule</c> configuration with its version.</summary>
    Task<SystemConfiguration?> FindScheduleConfigurationAsync(CancellationToken cancellationToken);

    Task<MapDistanceCache?> FindDistanceAsync(
        Guid warehouseId, Guid retailerId, CancellationToken cancellationToken);

    Task<MapLocation?> FindLocationAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken);
}
