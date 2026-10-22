using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class GoodsReceiptApiTests
{
    [Fact]
    public async Task Receipt_create_and_approval_are_authorized_idempotent_and_available_through_graphQl()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestInventoryStore();
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IInventoryStore>();
                services.AddSingleton<IInventoryStore>(store);
            }));
        using var client = factory.CreateClient();
        var request = new
        {
            warehouseId = store.Warehouse.Id,
            lines = new[]
            {
                new { productId = store.Product.Id, sourceLotCode = "LÔ-NHẬP-THỬ", quantity = 20,
                    productionDate = "2026-10-01", expirationDate = "2027-04-30" }
            }
        };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/goods-receipts", request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/goods-receipts", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/inventory/expiry/health")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "receipt-api-test-0001");
        var first = await client.PostAsJsonAsync("/api/v1/goods-receipts", request);
        var repeated = await client.PostAsJsonAsync("/api/v1/goods-receipts", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.Single(store.Receipts);
        Assert.Null(store.Balance);
        Assert.Empty(store.Transactions);

        var pendingReceipt = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(GoodsReceiptStatuses.Pending, pendingReceipt.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, pendingReceipt.GetProperty("receivedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, pendingReceipt.GetProperty("lines")[0].GetProperty("batchId").ValueKind);
        Assert.Equal("2026-10-01", pendingReceipt.GetProperty("lines")[0].GetProperty("productionDate").GetString());
        var receiptId = pendingReceipt.GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/v1/goods-receipts/{receiptId}/approve", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        var approved = await client.PostAsync($"/api/v1/goods-receipts/{receiptId}/approve", null);
        var approvedAgain = await client.PostAsync($"/api/v1/goods-receipts/{receiptId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approvedAgain.StatusCode);
        Assert.Equal(20, store.Balance!.PhysicalQuantity);
        Assert.Equal(20, store.Balance.AvailableQuantity);
        Assert.Equal(InventoryTransactionSourceTypes.GoodsReceipt,
            Assert.Single(store.Transactions).SourceType);

        var inventory = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory");
        var inventoryItem = inventory[0];
        Assert.False(inventoryItem.TryGetProperty("heldQuantity", out _));
        Assert.Equal(20m, inventoryItem.GetProperty("onHandQuantity").GetDecimal());
        Assert.Equal(20m, inventoryItem.GetProperty("availableQuantity").GetDecimal());

        var graphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($id: UUID!) { goodsReceipt(id: $id) { id receiptCode status approvedByUserId receivedAt lines { receivedQuantity productionDate batchCode } } }",
            variables = new { id = receiptId }
        });
        Assert.Equal(HttpStatusCode.OK, graphQl.StatusCode);
        var graphQlJson = JsonDocument.Parse(await graphQl.Content.ReadAsStringAsync());
        Assert.False(graphQlJson.RootElement.TryGetProperty("errors", out _), graphQlJson.RootElement.ToString());
        Assert.Equal(20m, graphQlJson.RootElement.GetProperty("data").GetProperty("goodsReceipt")
            .GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());

        var inventoryGraphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { inventory(first: 10) { nodes { onHandQuantity reservedQuantity availableQuantity } } }"
        });
        var inventoryGraphQlJson = JsonDocument.Parse(await inventoryGraphQl.Content.ReadAsStringAsync());
        Assert.False(inventoryGraphQlJson.RootElement.TryGetProperty("errors", out _), inventoryGraphQlJson.RootElement.ToString());
        Assert.Equal(20m, inventoryGraphQlJson.RootElement.GetProperty("data").GetProperty("inventory")
            .GetProperty("nodes")[0].GetProperty("availableQuantity").GetDecimal());

        var health = await client.GetAsync("/api/v1/inventory/expiry/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var healthGraphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { inventoryExpiryHealth { schedulerEnabled scheduleTimeZone scheduleLocalTime pendingOutboxMessages failedOutboxMessages } }"
        });
        Assert.Equal(HttpStatusCode.OK, healthGraphQl.StatusCode);
        var healthJson = JsonDocument.Parse(await healthGraphQl.Content.ReadAsStringAsync());
        Assert.False(healthJson.RootElement.TryGetProperty("errors", out _), healthJson.RootElement.ToString());
        Assert.Equal("Asia/Ho_Chi_Minh", healthJson.RootElement.GetProperty("data")
            .GetProperty("inventoryExpiryHealth").GetProperty("scheduleTimeZone").GetString());

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.WarehouseKeeper, identityFactory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "receipt-api-test-keeper-0001");
        var keeperReceipt = await client.PostAsJsonAsync("/api/v1/goods-receipts", request);
        Assert.Equal(HttpStatusCode.Created, keeperReceipt.StatusCode);
        Assert.Equal(2, store.Receipts.Count);
        var keeperReceiptId = JsonDocument.Parse(await keeperReceipt.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/v1/goods-receipts/{keeperReceiptId}/approve", null)).StatusCode);
        Assert.Equal(40, store.Balance.PhysicalQuantity);
    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestInventoryStore : IInventoryStore
    {
        public Warehouse Warehouse { get; } = new()
        {
            Id = Guid.NewGuid(),
            Code = "WH-TA",
            Name = "Kho Tân An",
            IsActive = true
        };
        public Product Product { get; } = new()
        {
            Id = Guid.NewGuid(),
            Sku = "SKU001",
            Name = "Sữa tươi tiệt trùng",
            Status = "Published",
            NearDateThresholdHours = 168
        };
        public List<GoodsReceipt> Receipts { get; } = [];
        public List<GoodsReceiptLine> Lines { get; } = [];
        public List<Batch> Batches { get; } = [];
        public List<InventoryTransaction> Transactions { get; } = [];
        public InventoryBalance? Balance { get; private set; }

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(warehouseId == Warehouse.Id);
        public Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult<Warehouse?>(warehouseId == Warehouse.Id ? Warehouse : null);
        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<Product?>(productId == Product.Id ? Product : null);
        public Task<GoodsReceiptView?> FindReceiptByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Receipts.SingleOrDefault(item => item.IdempotencyKey == key)));
        public Task<GoodsReceiptView?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Receipts.SingleOrDefault(item => item.Id == receiptId)));
        public Task<GoodsReceiptApprovalView?> FindReceiptForApprovalAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            var receipt = Receipts.SingleOrDefault(item => item.Id == receiptId);
            return Task.FromResult(receipt is null ? null : new GoodsReceiptApprovalView(receipt, Warehouse,
                Lines.Where(item => item.GoodsReceiptId == receiptId)
                    .Select(item => new GoodsReceiptApprovalLine(item, Product)).ToArray()));
        }
        public Task<IReadOnlyCollection<GoodsReceiptView>> ListReceiptsAsync(Guid actorUserId, string actorRole, Guid? warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<GoodsReceiptView>>(Receipts.Select(item => ToView(item)!).ToArray());
        public Task<Batch?> FindBatchForUpdateAsync(Guid warehouseId, Guid productId, DateOnly expirationDate, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.SingleOrDefault(item => item.WarehouseId == warehouseId && item.ProductId == productId && item.ExpirationDate == expirationDate));
        public Task<InventoryBalance?> FindBalanceForUpdateAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(Balance?.BatchId == batchId ? Balance : null);
        public Task<IReadOnlyCollection<BatchInventoryView>> ListInventoryAsync(Guid actorUserId, string actorRole, Guid? warehouseId, Guid? productId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<BatchInventoryView>>(Balance is null ? [] : [
                new BatchInventoryView(Batches.Single(), Balance, Warehouse.Code, Warehouse.Name,
                    Product.Sku, Product.Name, Product.NearDateThresholdHours)
            ]);
        public Task<BatchInventoryView?> FindBatchInventoryAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<BatchInventoryView?>(Balance?.BatchId == batchId
                ? new BatchInventoryView(Batches.Single(), Balance, Warehouse.Code, Warehouse.Name,
                    Product.Sku, Product.Name, Product.NearDateThresholdHours)
                : null);
        public Task<IReadOnlyCollection<InventoryHistoryView>> ListHistoryAsync(Guid actorUserId, string actorRole, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<InventoryHistoryView>>([]);
        public Task<ExpirySweepRun?> FindLatestExpirySweepRunAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ExpirySweepRun?>(null);
        public Task<int> CountOutboxMessagesAsync(string eventType, string status, CancellationToken cancellationToken) =>
            Task.FromResult(0);
        public Task<string> NextReceiptCodeAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
            Task.FromResult($"PN-{businessDate:yyMMdd}-01");
        public void AddReceipt(GoodsReceipt receipt) => Receipts.Add(receipt);
        public void AddReceiptLine(GoodsReceiptLine line) => Lines.Add(line);
        public void AddBatch(Batch batch) => Batches.Add(batch);
        public void AddBalance(InventoryBalance balance) => Balance = balance;
        public void AddTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
        public void ClearTracking() { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private GoodsReceiptView? ToView(GoodsReceipt? receipt)
        {
            if (receipt is null) return null;
            var lines = Lines.Where(item => item.GoodsReceiptId == receipt.Id).Select(line =>
            {
                var batch = Batches.SingleOrDefault(item => item.WarehouseId == receipt.WarehouseId &&
                    item.ProductId == line.ProductId && item.ExpirationDate == line.ExpirationDate);
                return new GoodsReceiptLineView(line, Product.Sku, Product.Name, batch?.Id, batch?.BatchCode);
            }).ToArray();
            return new GoodsReceiptView(receipt, Warehouse.Code, Warehouse.Name, lines);
        }
    }
}
