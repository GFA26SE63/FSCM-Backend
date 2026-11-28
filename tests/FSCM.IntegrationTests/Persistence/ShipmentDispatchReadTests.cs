using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Payments;
using FSCM.Persistence.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace FSCM.IntegrationTests.Persistence;

public sealed class ShipmentDispatchReadTests(ITestOutputHelper output)
{
    [ConfiguredDatabaseFact]
    public async Task Batch_read_matches_authoritative_single_order_rules_with_constant_query_count()
    {
        var counter = new QueryCounter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(AppDatabaseConfiguration.ReadConnectionString()).AddInterceptors(counter).Options);
        var store = new ShipmentDispatchReadStore(db);
        var clock = Stopwatch.StartNew();
        var pickings = await store.ListPickingAsync(Guid.Empty, SystemRoles.Admin, default);
        output.WriteLine($"Projected picking list: {pickings.Count} rows, {counter.Count} SQL reads, {clock.ElapsedMilliseconds} ms");
        Assert.Equal(2, counter.Count);
        var ids = pickings.Select(p => p.OrderId).Distinct().Take(50).ToArray();
        Assert.NotEmpty(ids);
        counter.Count = 0;
        clock.Restart();
        var service = new ShipmentDispatchReadService(store);
        var rows = await service.ReadAsync(ids, Guid.Empty, SystemRoles.Admin, default);
        output.WriteLine($"Batch readiness: {rows.Count} orders, {counter.Count} SQL reads, {clock.ElapsedMilliseconds} ms");
        Assert.Equal(4, counter.Count);
        var original = new ShipmentService(new ShipmentStore(db), new PaymentStore(db), null!, null!, TimeProvider.System);
        foreach (var row in rows.Take(3))
        {
            var expected = await original.GetOrderReadinessAsync(row.SalesOrderId, Guid.Empty, SystemRoles.Admin, default);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(row));
        }
        var keeper = Guid.NewGuid(); // no assignments; no global fallback
        Assert.Empty(await store.ListPickingAsync(keeper, SystemRoles.WarehouseKeeper, default));
        await Assert.ThrowsAsync<ShipmentOperationException>(() => service.ReadAsync(ids, keeper, SystemRoles.WarehouseKeeper, default));
        await Assert.ThrowsAsync<ShipmentOperationException>(() => service.ReadAsync(new Guid[51], Guid.Empty, SystemRoles.Admin, default));
        Assert.Empty(await service.ReadAsync([], Guid.Empty, SystemRoles.Admin, default));
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
