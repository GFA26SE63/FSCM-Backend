using FSCM.Application.Features.InventoryLocks;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.InventoryLocks;

public sealed class InventoryLockServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_locks_unreserved_quantity_without_resolution_flag()
    {
        var fixture = new Fixture(physical: 30, reserved: 0, activeLocked: 0);

        var result = await fixture.Service.CreateAsync(fixture.KeeperId, SystemRoles.WarehouseKeeper,
            new CreateInventoryLockData(fixture.Batch.Id, 10, "Damaged", "Thùng móp"), default);

        Assert.Equal(InventoryLockStatuses.Active, result.Status);
        Assert.Equal(InventoryLockReasons.Damaged, result.Reason);
        Assert.False(result.RequiresResolution);
        Assert.Single(fixture.Store.Locks);
    }

    [Fact]
    public async Task Create_flags_resolution_when_lock_overlaps_reserved()
    {
        // 30 on-hand, 25 reserved → only 5 free. Locking 10 overlaps reserved.
        var fixture = new Fixture(physical: 30, reserved: 25, activeLocked: 0);

        var result = await fixture.Service.CreateAsync(fixture.KeeperId, SystemRoles.WarehouseKeeper,
            new CreateInventoryLockData(fixture.Batch.Id, 10, "Expired", null), default);

        Assert.True(result.RequiresResolution);
    }

    [Fact]
    public async Task Create_rejects_quantity_above_on_hand()
    {
        var fixture = new Fixture(physical: 30, reserved: 0, activeLocked: 25);

        var exception = await Assert.ThrowsAsync<InventoryLockOperationException>(() =>
            fixture.Service.CreateAsync(fixture.KeeperId, SystemRoles.WarehouseKeeper,
                new CreateInventoryLockData(fixture.Batch.Id, 10, "Damaged", null), default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Create_rejects_invalid_reason()
    {
        var fixture = new Fixture(physical: 30, reserved: 0, activeLocked: 0);

        var exception = await Assert.ThrowsAsync<InventoryLockOperationException>(() =>
            fixture.Service.CreateAsync(fixture.KeeperId, SystemRoles.WarehouseKeeper,
                new CreateInventoryLockData(fixture.Batch.Id, 5, "Nonsense", null), default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Create_forbidden_for_sales_role()
    {
        var fixture = new Fixture(physical: 30, reserved: 0, activeLocked: 0);

        var exception = await Assert.ThrowsAsync<InventoryLockOperationException>(() =>
            fixture.Service.CreateAsync(Guid.NewGuid(), SystemRoles.Sales,
                new CreateInventoryLockData(fixture.Batch.Id, 5, "Damaged", null), default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Release_marks_lock_released()
    {
        var fixture = new Fixture(physical: 30, reserved: 0, activeLocked: 0);
        var created = await fixture.Service.CreateAsync(fixture.KeeperId, SystemRoles.WarehouseKeeper,
            new CreateInventoryLockData(fixture.Batch.Id, 10, "Damaged", null), default);

        var released = await fixture.Service.ReleaseAsync(created.Id, fixture.KeeperId, SystemRoles.WarehouseKeeper,
            new ReleaseInventoryLockData(created.Version, "Nhầm, mở khóa"), default);

        Assert.Equal(InventoryLockStatuses.Released, released.Status);
        Assert.Equal(fixture.KeeperId, released.ReleasedByUserId);
        Assert.NotNull(released.ReleasedAt);
    }

    [Fact]
    public async Task Release_rejects_stale_version()
    {
        var fixture = new Fixture(physical: 30, reserved: 0, activeLocked: 0);
        var created = await fixture.Service.CreateAsync(fixture.KeeperId, SystemRoles.WarehouseKeeper,
            new CreateInventoryLockData(fixture.Batch.Id, 10, "Damaged", null), default);

        var exception = await Assert.ThrowsAsync<InventoryLockOperationException>(() =>
            fixture.Service.ReleaseAsync(created.Id, fixture.KeeperId, SystemRoles.WarehouseKeeper,
                new ReleaseInventoryLockData(created.Version + 5, null), default));

        Assert.Equal("conflict", exception.Code);
    }

    private sealed class Fixture
    {
        public Guid KeeperId { get; } = Guid.NewGuid();
        public Batch Batch { get; }
        public FakeStore Store { get; }
        public InventoryLockService Service { get; }

        public Fixture(decimal physical, decimal reserved, decimal activeLocked)
        {
            Batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                BatchCode = "WH-HCM-01-SKU001-30042027"
            };
            Store = new FakeStore(Batch, physical, reserved, activeLocked);
            Service = new InventoryLockService(Store, new FixedTimeProvider(Now));
        }
    }

    private sealed class FakeStore(Batch batch, decimal physical, decimal reserved, decimal activeLocked)
        : IInventoryLockStore
    {
        public List<InventoryLock> Locks { get; } = [];

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<BatchLockContext?> FindBatchContextAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<BatchLockContext?>(batchId == batch.Id
                ? new BatchLockContext(batch, "WH-HCM-01", "SKU001", physical, reserved, activeLocked)
                : null);

        public Task<InventoryLock?> FindForUpdateAsync(Guid lockId, CancellationToken cancellationToken) =>
            Task.FromResult(Locks.SingleOrDefault(item => item.Id == lockId));

        public Task<InventoryLockView?> FindAsync(Guid lockId, CancellationToken cancellationToken)
        {
            var found = Locks.SingleOrDefault(item => item.Id == lockId);
            return Task.FromResult(found is null ? null : new InventoryLockView(found, batch.BatchCode, "WH-HCM-01", "SKU001"));
        }

        public Task<IReadOnlyCollection<InventoryLockView>> ListAsync(
            Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, string? status,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<InventoryLockView>>(
                Locks.Select(item => new InventoryLockView(item, batch.BatchCode, "WH-HCM-01", "SKU001")).ToArray());

        public void Add(InventoryLock inventoryLock) => Locks.Add(inventoryLock);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
