using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.IntegrationTests.Persistence;

public sealed class DisposalPostgresFactAttribute : FactAttribute
{
    public DisposalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FSCM_DISPOSAL_TEST_POSTGRES")))
            Skip = "Set FSCM_DISPOSAL_TEST_POSTGRES to run disposal ledger tests on an isolated database.";
    }
}

public sealed class DisposalPostgresTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);
    }

    [DisposalPostgresFact]
    public async Task Disposal_is_scoped_atomic_replayable_and_preserves_ledger_locks_and_QR_history()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_DISPOSAL_TEST_POSTGRES"));
        Assert.True(connection.Host is "127.0.0.1" or "localhost");
        connection.Database = $"fscm_disposal_test_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options;
        AppDbContext Open() => new(options);
        var clock = new Clock();
        var now = clock.GetUtcNow();
        var today = new DateOnly(2026, 11, 15);
        await using var db = Open();
        Assert.True(await db.Database.EnsureCreatedAsync());
        var region = new Region { Id = Guid.NewGuid(), Code = "TEST", Name = "Test", IsActive = true };
        var adminRole = new RoleDefinition { Id = Guid.NewGuid(), Code = SystemRoles.Admin,
            Name = "Admin", BaseRole = SystemRoles.Admin, IsSystem = true, CreatedAt = now, UpdatedAt = now };
        var keeperRole = new RoleDefinition { Id = Guid.NewGuid(), Code = SystemRoles.WarehouseKeeper,
            Name = "Keeper", BaseRole = SystemRoles.WarehouseKeeper, IsSystem = true, CreatedAt = now, UpdatedAt = now };
        db.AddRange(adminRole, keeperRole);
        var admin = new UserAccount { Id = Guid.NewGuid(), Email = "admin@test.local", FullName = "Admin",
            Role = SystemRoles.Admin, RoleDefinition = adminRole, IsActive = true, IdentityProvider = "Local" };
        var keeper = new UserAccount { Id = Guid.NewGuid(), Email = "keeper@test.local", FullName = "Keeper",
            Role = SystemRoles.WarehouseKeeper, RoleDefinition = keeperRole, IsActive = true, IdentityProvider = "Local" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = "WH-A", Name = "A", RegionId = region.Id, IsActive = true };
        var other = new Warehouse { Id = Guid.NewGuid(), Code = "WH-B", Name = "B", RegionId = region.Id, IsActive = true };
        var category = new ProductCategory { Id = Guid.NewGuid(), Code = "CAT", Name = "Category", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), ProductCategoryId = category.Id, Sku = "SKU-TEST",
            Name = "Test", Unit = "Carton", Status = "Published", ShelfLifeHours = 240, NearDateThresholdHours = 24 };
        db.AddRange(region, admin, keeper, warehouse, other, category, product);
        db.UserWarehouseAssignmentRecords.Add(new UserWarehouseAssignment { Id = Guid.NewGuid(),
            UserAccountId = keeper.Id, WarehouseId = warehouse.Id, IsActive = true });
        Batch AddBatch(string code, Warehouse wh, DateOnly expiry, decimal quantity, decimal reserved = 0)
        {
            var batch = new Batch { Id = Guid.NewGuid(), WarehouseId = wh.Id, ProductId = product.Id,
                BatchCode = code, Quantity = quantity, ProductionDate = expiry.AddDays(-30 - code.Length),
                ExpirationDate = expiry, ExpiryStatus = "Expired", StockStatus = "InStock" };
            db.BatchRecords.Add(batch);
            db.InventoryBalanceRecords.Add(new InventoryBalance { Id = Guid.NewGuid(), BatchId = batch.Id,
                PhysicalQuantity = quantity, ReservedQuantity = reserved, AvailableQuantity = quantity - reserved });
            return batch;
        }
        var a = AddBatch("EXPIRED-A", warehouse, today.AddDays(-1), 10);
        var b = AddBatch("EXPIRED-BB", other, today.AddDays(-1), 7);
        var reserved = AddBatch("RESERVED", warehouse, today.AddDays(-2), 5, 2);
        var future = AddBatch("FUTURE", warehouse, today.AddDays(5), 6);
        AddBatch("TODAY", warehouse, today, 3);
        var concurrent = AddBatch("CONCURRENT", warehouse, today.AddDays(-3), 20);
        var label = new BatchQrLabel { Id = Guid.NewGuid(), BatchId = a.Id, CurrentWarehouseId = warehouse.Id,
            QrCode = "CID-TEST", Quantity = 10, Status = QrLabelStatuses.Available, GeneratedAt = now, UpdatedAt = now };
        var inventoryLock = new InventoryLock { Id = Guid.NewGuid(), BatchId = a.Id, WarehouseId = warehouse.Id,
            ProductId = product.Id, LockedQuantity = 10, Status = InventoryLockStatuses.Active,
            Reason = InventoryLockReasons.Expired, CreatedAt = now, UpdatedAt = now };
        db.AddRange(label, inventoryLock);
        await db.SaveChangesAsync();
        async Task<T> Call<T>(Func<DisposalService, Task<T>> action)
        {
            await using var request = Open();
            return await action(new DisposalService(request, clock));
        }
        var candidates = await Call(s => s.CandidatesAsync(keeper.Id, keeper.Role, default));
        Assert.Contains(candidates, c => c.BatchCode == a.BatchCode);
        Assert.DoesNotContain(candidates, c => new[] { b.BatchCode, reserved.BatchCode, future.BatchCode, "TODAY" }.Contains(c.BatchCode));
        await Assert.ThrowsAsync<InventoryOperationException>(() => Call(s => s.CreateAsync(
            new([b.BatchCode], "Tiêu hủy tại kho", Guid.NewGuid()), keeper.Id, keeper.Role, default)));
        await Assert.ThrowsAsync<InventoryOperationException>(() => Call(s => s.CreateAsync(
            new([a.BatchCode, future.BatchCode], "Tiêu hủy tại kho", Guid.NewGuid()), admin.Id, admin.Role, default)));
        Assert.Empty(await Call(s => s.ListAsync(admin.Id, admin.Role, null, default)));
        var input = new DisposalInput([a.BatchCode, b.BatchCode], "Tiêu hủy tại kho", Guid.NewGuid());
        var created = await Call(s => s.CreateAsync(input, admin.Id, admin.Role, default));
        Assert.Equal(2, created.Length);
        Assert.Equal(17, created.Sum(r => r.TotalQty));
        var replay = await Call(s => s.CreateAsync(input, admin.Id, admin.Role, default));
        Assert.Equal(created.Select(r => r.Code).Order(), replay.Select(r => r.Code).Order());
        Assert.Single(await Call(s => s.ListAsync(keeper.Id, keeper.Role, null, default)));
        await Assert.ThrowsAsync<InventoryOperationException>(() => Call(s => s.CreateAsync(
            input with { BatchCodes = [a.BatchCode] }, admin.Id, admin.Role, default)));
        await Assert.ThrowsAsync<InventoryOperationException>(() => Call(s => s.CreateAsync(
            input with { OperationId = Guid.NewGuid() }, admin.Id, admin.Role, default)));
        await using var verify = Open();
        Assert.Equal(0, await verify.InventoryBalanceRecords.Where(x => x.BatchId == a.Id || x.BatchId == b.Id)
            .SumAsync(x => x.PhysicalQuantity));
        Assert.Equal(2, await verify.Set<InventoryTransaction>().CountAsync());
        Assert.Equal(-17, await verify.Set<InventoryTransaction>().SumAsync(x => x.PhysicalQuantityDelta));
        Assert.Equal(InventoryLockStatuses.WrittenOff, (await verify.Set<InventoryLock>().SingleAsync()).Status);
        Assert.Equal(QrLabelStatuses.Invalidated, (await verify.Set<BatchQrLabel>().SingleAsync()).Status);
        Assert.Single(await verify.Set<QrLabelEvent>().ToArrayAsync());
        async Task<bool> Race()
        {
            try
            {
                await Call(s => s.CreateAsync(new([concurrent.BatchCode], "Tiêu hủy tại kho", Guid.NewGuid()),
                    admin.Id, admin.Role, default));
                return true;
            }
            catch (InventoryOperationException) { return false; }
        }
        var races = await Task.WhenAll(Race(), Race());
        Assert.Single(races, x => x);
        Assert.Equal(-20, await verify.Set<InventoryTransaction>().Where(x => x.BatchId == concurrent.Id)
            .SumAsync(x => x.PhysicalQuantityDelta));
    }
}
