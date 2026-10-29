using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// Stores with nothing published, for tests that must construct the planning collaborators without
/// exercising them. Every read returns "not configured" so an accidental call fails clearly.
/// </summary>
internal sealed class EmptyTransitTableStore : ITransitTableStore
{
    public Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken) => Task.FromResult<TransitTableView?>(null);
    public Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) => Task.FromResult<TransitTableView?>(null);
    public Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) => Task.FromResult<TransitTableView?>(null);
    public Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<TransitTableView>>([]);
    public Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) => Task.FromResult<TransitTable?>(null);
    public Task<int> MaxVersionAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    public void AddTable(TransitTable table) { }
    public void AddBands(IEnumerable<TransitTableBand> bands) { }
    public void AddAuditLog(AuditLog auditLog) { }
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EmptyDiscountStore : IShelfLifeDiscountStore
{
    public Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken cancellationToken) => Task.FromResult<ShelfLifeDiscountTableView?>(null);
    public Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) => Task.FromResult<ShelfLifeDiscountTableView?>(null);
    public Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) => Task.FromResult<ShelfLifeDiscountTableView?>(null);
    public Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShelfLifeDiscountTableView>>([]);
    public Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) => Task.FromResult<ShelfLifeDiscountTable?>(null);
    public Task<int> MaxVersionAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    public void AddTable(ShelfLifeDiscountTable table) { }
    public void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers) { }
    public void AddAuditLog(AuditLog auditLog) { }
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EmptyEtaStore : IDeliveryEtaStore
{
    public Task<SystemConfiguration?> FindScheduleConfigurationAsync(CancellationToken cancellationToken) => Task.FromResult<SystemConfiguration?>(null);
    public Task<MapDistanceCache?> FindDistanceAsync(Guid warehouseId, Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<MapDistanceCache?>(null);
    public Task<MapLocation?> FindLocationAsync(string entityType, Guid entityId, CancellationToken cancellationToken) => Task.FromResult<MapLocation?>(null);
}
