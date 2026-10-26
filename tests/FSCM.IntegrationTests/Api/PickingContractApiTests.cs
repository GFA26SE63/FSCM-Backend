using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class PickingContractApiTests : IClassFixture<IdentityAuthorizationTests.IdentityApiFactory>
{
    private readonly IdentityAuthorizationTests.IdentityApiFactory identityFactory;

    public PickingContractApiTests(IdentityAuthorizationTests.IdentityApiFactory identityFactory) =>
        this.identityFactory = identityFactory;

    [Fact]
    public async Task Canonical_scan_and_complete_routes_enforce_role_and_preserve_price_link()
    {
        var store = new PickingContractStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var scanBody = new
        {
            qrCode = store.Label.QrCode,
            quantity = 5,
            expectedPickingVersion = 0,
            expectedLabelVersion = 1,
            splits = new[] { new { allocationRunAllocationId = store.Split.AllocationRunAllocationId, quantity = 5 } }
        };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PostScan(client, store.List.PickingCode, scanBody)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.CreateToken(identityFactory.Sales));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostScan(client, store.List.PickingCode, scanBody)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(
            identityFactory.CreateToken(identityFactory.WarehouseKeeper));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/v1/picking-lists/{store.List.PickingCode}/scan", scanBody)).StatusCode);

        var scan = await PostScan(client, store.List.PickingCode, scanBody);
        var scanText = await scan.Content.ReadAsStringAsync();
        Assert.True(scan.StatusCode == HttpStatusCode.OK, scanText);
        var scanJson = JsonDocument.Parse(scanText).RootElement;
        var split = scanJson.GetProperty("gathers")[0].GetProperty("splits")[0];
        Assert.Equal(store.PriceId, split.GetProperty("allocationPlanPriceId").GetGuid());
        Assert.Equal(5, split.GetProperty("pickedQuantity").GetDecimal());
        Assert.Empty(store.InventoryIssues);

        var complete = await client.PostAsJsonAsync(
            $"/api/v1/picking-lists/{store.List.PickingCode}/complete",
            new { expectedPickingVersion = 1, exceptionType = (string?)null, note = (string?)null });
        var completeText = await complete.Content.ReadAsStringAsync();
        Assert.True(complete.StatusCode == HttpStatusCode.OK, completeText);
        Assert.Equal(RunPickingListStatuses.Ready,
            JsonDocument.Parse(completeText).RootElement.GetProperty("status").GetString());
        Assert.Single(store.Shipments);
        Assert.Empty(store.InventoryIssues);
    }

    [Fact]
    public async Task GraphQl_read_contract_exposes_the_immutable_allocation_price_link()
    {
        var store = new PickingContractStore();
        await using var factory = CreateFactory(store, new PickingEntityQueries(store.Split));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.CreateToken(identityFactory.Admin));

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { entities { runPickingSplits(first: 1) { nodes { allocationRunAllocationId allocationPlanPriceId quantity pickedQuantity } } } }"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.False(json.TryGetProperty("errors", out _), body);
        var split = json.GetProperty("data").GetProperty("entities")
            .GetProperty("runPickingSplits").GetProperty("nodes")[0];
        Assert.Equal(store.PriceId, split.GetProperty("allocationPlanPriceId").GetGuid());
    }

    private async Task<HttpResponseMessage> PostScan(HttpClient client, string code, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/picking-lists/{code}/scans")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", "f44-contract-scan-001");
        return await client.SendAsync(request);
    }

    private WebApplicationFactory<Program> CreateFactory(
        PickingContractStore store, IEntityQueryService? entityQueries = null) =>
        identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRunPickingStore>();
            services.AddSingleton<IRunPickingStore>(store);
            if (entityQueries is not null)
            {
                services.RemoveAll<IEntityQueryService>();
                services.AddSingleton(entityQueries);
            }
        }));

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private sealed class PickingContractStore : IRunPickingStore
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 25, 5, 0, 0, TimeSpan.Zero);
        public Guid PriceId { get; } = Guid.NewGuid();
        public RunPickingList List { get; }
        public RunPickingGather Gather { get; }
        public RunPickingSplit Split { get; }
        public BatchQrLabel Label { get; }
        public List<QrLabelEvent> Events { get; } = [];
        public List<Shipment> Shipments { get; } = [];
        public List<object> InventoryIssues { get; } = [];

        public PickingContractStore()
        {
            var warehouseId = Guid.NewGuid();
            var batchId = Guid.NewGuid();
            List = new RunPickingList
            {
                Id = Guid.NewGuid(), PickingCode = "PS-DOT-20261025-01-01",
                AllocationRunId = Guid.NewGuid(), AllocationPlanVersionId = Guid.NewGuid(),
                WarehouseId = warehouseId, Status = RunPickingListStatuses.Issued,
                IssuedAt = Now, CreatedAt = Now, UpdatedAt = Now
            };
            Gather = new RunPickingGather
            {
                Id = Guid.NewGuid(), RunPickingListId = List.Id, ProductId = Guid.NewGuid(),
                SkuSnapshot = "SKU-F44", BatchId = batchId, BatchCodeSnapshot = "LO-F44-01",
                RequiredQuantity = 5, Status = RunPickingGatherStatuses.Pending, CreatedAt = Now
            };
            Split = new RunPickingSplit
            {
                Id = Guid.NewGuid(), RunPickingGatherId = Gather.Id,
                AllocationRunAllocationId = Guid.NewGuid(), AllocationRunLineId = Guid.NewGuid(),
                AllocationPlanPriceId = PriceId, SalesOrderId = Guid.NewGuid(),
                SalesOrderLineId = Guid.NewGuid(), OrderCodeSnapshot = "DH-F44-001",
                Quantity = 5, CreatedAt = Now
            };
            Label = new BatchQrLabel
            {
                Id = Guid.NewGuid(), BatchId = batchId, CurrentWarehouseId = warehouseId,
                QrCode = "TEM-F44-001", Quantity = 5, Status = QrLabelStatuses.Available,
                Version = 1, GeneratedAt = Now, UpdatedAt = Now
            };
        }

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken ct) =>
            Task.FromResult(warehouseId == List.WarehouseId);
        public Task<IReadOnlyCollection<RunPickingWorkflowView>> ListAsync(Guid userId, string role,
            Guid? warehouseId, string? status, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<RunPickingWorkflowView>>([View]);
        public Task<RunPickingWorkflowView?> FindAsync(string pickingCode, bool forUpdate, CancellationToken ct) =>
            Task.FromResult<RunPickingWorkflowView?>(pickingCode == List.PickingCode ? View : null);
        public Task<QrLabelEvent?> FindOperationAsync(string operationKey, CancellationToken ct) =>
            Task.FromResult(Events.SingleOrDefault(x => x.OperationKey == operationKey));
        public Task<BatchQrLabel?> FindLabelForUpdateAsync(string qrCode, CancellationToken ct) =>
            Task.FromResult<BatchQrLabel?>(qrCode == Label.QrCode ? Label : null);
        public Task<string> NextQrCodeAsync(CancellationToken ct) => Task.FromResult("TEM-F44-002");
        public Task<string> NextShipmentCodeAsync(CancellationToken ct) => Task.FromResult("SHIP-F44-001");
        public Task<bool> ShipmentExistsAsync(Guid salesOrderId, Guid warehouseId, CancellationToken ct) =>
            Task.FromResult(Shipments.Any(x => x.SalesOrderId == salesOrderId && x.WarehouseId == warehouseId));
        public void AddLabel(BatchQrLabel label) { }
        public void AddLabelEvent(QrLabelEvent labelEvent) => Events.Add(labelEvent);
        public void AddShipment(Shipment shipment) => Shipments.Add(shipment);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct);
        public void ClearTracking() { }

        private RunPickingWorkflowView View => new(List, [Gather], [Split],
            Events.Where(x => x.EventType == QrLabelEventTypes.Picked)
                .Select(x => new RunPickingScanView(x, Label.QrCode)).ToArray());
    }

    private sealed class PickingEntityQueries(RunPickingSplit split) : IEntityQueryService
    {
        public IQueryable<TEntity> Query<TEntity>(EntityQueryAccess access) where TEntity : class =>
            typeof(TEntity) == typeof(RunPickingSplit)
                ? (IQueryable<TEntity>)(object)new[] { split }.AsQueryable()
                : Array.Empty<TEntity>().AsQueryable();
    }
}
