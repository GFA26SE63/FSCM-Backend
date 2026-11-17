using System.Net;
using System.Net.Http.Json;
using Fscm.Traceability.Data;
using Fscm.Traceability.Services;
using FSCM.Application.Features.Picking;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class FactoryInboundQueueTests
{
    [Fact]
    public void Factory_queue_query_restricts_status_and_destination_before_returning_rows()
    {
        using var db = new FactoryDbContext(new DbContextOptionsBuilder<FactoryDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var service = new ProductionService(db, null!, TimeProvider.System);
        var sql = service.DispatchedQuery([Guid.Parse("11111111-1111-1111-1111-111111111111")]).ToQueryString();
        Assert.Contains("Dispatched", sql);
        Assert.Contains("DestinationWarehouseId", sql);
        Assert.Contains("11111111-1111-1111-1111-111111111111", sql);
        Assert.Contains("FACTORY_CARTON", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public async Task Factory_queue_rejects_an_unscoped_or_invalid_request_without_reading_database()
    {
        var service = new ProductionService(null!, null!, TimeProvider.System);
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { Guid.Empty }, Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() })
            await Assert.ThrowsAsync<Fscm.Traceability.Models.FactoryException>(() => service.DispatchedAsync(ids, default));
    }

    [Fact]
    public async Task Gateway_sends_only_the_requested_warehouse_scope_and_deserializes_the_fixed_destination()
    {
        var warehouse = Guid.NewGuid();
        var handler = new QueueHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://factory.test/api/integration/dispatches?warehouseIds={warehouse:D}", request.RequestUri!.ToString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] {
                new FactoryInboundPallet(Guid.NewGuid(), "PID-1", warehouse, DateTimeOffset.UtcNow,
                    "BID-1", "SKU001", "Product", 6, new DateOnly(2027, 1, 1)) }) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://factory.test/") };
        var gateway = new FactoryHttpGateway(http);
        Assert.Empty(await gateway.DispatchedAsync([], default));
        Assert.Equal(0, handler.Calls);
        var result = await gateway.DispatchedAsync([warehouse, warehouse], default);
        Assert.Equal(warehouse, Assert.Single(result).DestinationWarehouseId);
        Assert.Null(result[0].ReceiptDocumentId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Admin_cannot_start_receiving_and_unrelated_roles_cannot_read_the_queue()
    {
        var service = new WarehouseWorkService(null!, null!, null!, null!, null!, TimeProvider.System, null!);
        var error = await Assert.ThrowsAsync<PickingOperationException>(() =>
            service.PrepareFactoryAsync(new("PID-1"), Guid.NewGuid(), "Admin", default));
        Assert.Equal("forbidden", error.Code);
        error = await Assert.ThrowsAsync<PickingOperationException>(() =>
            service.PendingFactoryAsync(Guid.NewGuid(), "Operator", default));
        Assert.Equal("forbidden", error.Code);
    }

    private sealed class QueueHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response(request));
        }
    }
}
