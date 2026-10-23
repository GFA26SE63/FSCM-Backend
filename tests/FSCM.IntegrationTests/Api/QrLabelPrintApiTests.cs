using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class QrLabelPrintApiTests
{
    [Fact]
    public async Task Print_endpoints_enforce_auth_and_return_lineage_quantity_contract()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestQrLabelStore();
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQrLabelStore>();
                services.AddSingleton<IQrLabelStore>(store);
                services.RemoveAll<IQrCodeRenderer>();
                services.AddSingleton<IQrCodeRenderer, TestQrRenderer>();
            }));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/qr-labels/{store.Parent.QrCode}/print")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/qr-labels/{store.Parent.QrCode}/print")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        var single = await client.GetAsync($"/api/v1/qr-labels/{store.Parent.QrCode}/print");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        Assert.Equal("text/html", single.Content.Headers.ContentType!.MediaType);
        var html = await single.Content.ReadAsStringAsync();
        Assert.Contains(store.Parent.QrCode, html);
        Assert.Contains("70 thùng", html);
        Assert.Contains("<svg", html);
        Assert.Contains(TestQrLabelStore.ReceiptCode, html);

        var batch = await client.GetAsync($"/api/v1/qr-labels/batches/{store.Batch.Id}/print");
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        var batchHtml = await batch.Content.ReadAsStringAsync();
        Assert.Contains(store.Parent.QrCode, batchHtml);
        Assert.Contains(store.Child.QrCode, batchHtml);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/qr-labels/{store.Child.QrCode}");
        Assert.Equal(2, detail.GetProperty("lineage").GetArrayLength());
        Assert.Equal(100m, detail.GetProperty("lineage").EnumerateArray()
            .Sum(item => item.GetProperty("quantity").GetDecimal()));
    }

    [Fact]
    public async Task GraphQl_qr_read_uses_same_lineage_contract()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestQrLabelStore();
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQrLabelStore>();
                services.AddSingleton<IQrLabelStore>(store);
                services.RemoveAll<IQrCodeRenderer>();
                services.AddSingleton<IQrCodeRenderer, TestQrRenderer>();
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($code: String!) { qrLabel(code: $code) { label { qrCode quantity } lineage { qrCode quantity parentQrCode } } }",
            variables = new { code = store.Child.QrCode }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.Equal(2, json.RootElement.GetProperty("data").GetProperty("qrLabel")
            .GetProperty("lineage").GetArrayLength());
    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestQrRenderer : IQrCodeRenderer
    {
        public string RenderSvg(string payload) => $"<svg data-code=\"{payload}\"></svg>";
        public byte[] RenderPng(string payload, int pixelsPerModule = 10) => [];
    }

    private sealed class TestQrLabelStore : IQrLabelStore
    {
        private readonly Warehouse warehouse = new()
        {
            Id = Guid.NewGuid(),
            Code = "WH-TA",
            Name = "Kho Tân An",
            IsActive = true
        };
        private readonly Product product = new()
        {
            Id = Guid.NewGuid(),
            Sku = "SKU001",
            Name = "Sữa tươi tiệt trùng"
        };

        public TestQrLabelStore()
        {
            Batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                ProductId = product.Id,
                BatchCode = "WH-TA-SKU001-30112026",
                ExpirationDate = new DateOnly(2026, 11, 30)
            };
            var rootId = Guid.NewGuid();
            Parent = new BatchQrLabel
            {
                Id = rootId,
                RootLabelId = rootId,
                BatchId = Batch.Id,
                CurrentWarehouseId = warehouse.Id,
                QrCode = "TEM-000204",
                Quantity = 70,
                Status = QrLabelStatuses.Available,
                Version = 2,
                GeneratedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Child = new BatchQrLabel
            {
                Id = Guid.NewGuid(),
                ParentLabelId = Parent.Id,
                RootLabelId = Parent.Id,
                BatchId = Batch.Id,
                CurrentWarehouseId = warehouse.Id,
                QrCode = "TEM-000205",
                Quantity = 30,
                Status = QrLabelStatuses.Available,
                Version = 1,
                GeneratedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        public Batch Batch { get; }
        public BatchQrLabel Parent { get; }
        public BatchQrLabel Child { get; }
        private IReadOnlyCollection<BatchQrLabel> Labels => [Parent, Child];

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(warehouseId == warehouse.Id);
        public Task<QrLabelView?> FindByCodeAsync(string qrCode, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Labels.SingleOrDefault(item => item.QrCode == qrCode)));
        public Task<BatchQrLabel?> FindForUpdateAsync(string qrCode, CancellationToken cancellationToken) =>
            Task.FromResult(Labels.SingleOrDefault(item => item.QrCode == qrCode));
        public Task<QrLabelView?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Labels.SingleOrDefault(item => item.Id == id)));
        public Task<IReadOnlyCollection<QrLabelView>> ListAsync(Guid userId, string role, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QrLabelView>>(Labels
                .Where(item => (!warehouseId.HasValue || item.CurrentWarehouseId == warehouseId) &&
                               (!batchId.HasValue || item.BatchId == batchId))
                .Select(item => ToView(item)!).ToArray());
        public Task<IReadOnlyCollection<QrLabelView>> ListLineageAsync(Guid rootLabelId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QrLabelView>>(Labels
                .Where(item => item.Id == rootLabelId || item.RootLabelId == rootLabelId)
                .Select(item => ToView(item)!).ToArray());
        public Task<IReadOnlyCollection<QrLabelEventView>> ListEventsAsync(Guid rootLabelId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QrLabelEventView>>([]);
        public Task<QrLabelEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult<QrLabelEvent?>(null);
        public Task<QrLabelEvent?> FindLatestEventAsync(Guid labelId, CancellationToken cancellationToken) =>
            Task.FromResult<QrLabelEvent?>(null);
        public Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<Batch?>(Batch.Id == batchId ? Batch : null);
        public Task<Guid?> FindGoodsReceiptIdForBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);
        public Task<InventoryBalance?> FindBalanceAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<InventoryBalance?>(new() { BatchId = batchId, PhysicalQuantity = 100, AvailableQuantity = 100 });
        public Task<decimal> SumActiveQuantityAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(Labels.Where(item => item.BatchId == batchId &&
                item.Status is QrLabelStatuses.Available or QrLabelStatuses.Picked).Sum(item => item.Quantity));
        public Task<string> NextQrCodeAsync(CancellationToken cancellationToken) => Task.FromResult("TEM-999999");
        public void AddLabel(BatchQrLabel label) => throw new NotSupportedException();
        public void AddEvent(QrLabelEvent labelEvent) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public const string ReceiptCode = "GRN-000042";

        private QrLabelView? ToView(BatchQrLabel? label) => label is null ? null : new(
            label, Batch, warehouse, product,
            label.ParentLabelId == Parent.Id ? Parent.QrCode : null, Parent.QrCode, ReceiptCode);
    }
}
