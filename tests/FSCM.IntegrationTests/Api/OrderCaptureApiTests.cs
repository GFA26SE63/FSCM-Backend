using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Promotions;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class OrderCaptureApiTests
{
    [Fact]
    public async Task Pricing_snapshot_rest_and_graphql_never_apply_client_batch_discount()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestQuoteStore();
        var orderStore = new TestOrderStore(identityFactory.Sales.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPromotionStore>();
                services.AddSingleton<IPromotionStore>(store);
                services.RemoveAll<IOrderStore>();
                services.AddSingleton<IOrderStore>(orderStore);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);

        var rest = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/pricing/snapshot?productId={store.Product.Id}&quantity=10&retailerId={orderStore.Retailer.Id}&batchId={store.Batch.Id}");
        Assert.True(rest.GetProperty("finalPricePendingAllocation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("batchId").ValueKind);
        Assert.Equal(8m, rest.GetProperty("discountPercent").GetDecimal());

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($productId: UUID!, $retailerId: UUID!) { priceSnapshot(productId: $productId, retailerId: $retailerId, quantity: 10) { batchId discountPercent finalPricePendingAllocation appliedPromotion { promotionType } } }",
            variables = new { productId = store.Product.Id, retailerId = orderStore.Retailer.Id }
        });
        var graphQl = JsonDocument.Parse(await graphQlResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.False(graphQl.TryGetProperty("errors", out _), graphQl.ToString());
        var quote = graphQl.GetProperty("data").GetProperty("priceSnapshot");
        Assert.True(quote.GetProperty("finalPricePendingAllocation").GetBoolean());
        Assert.Equal(PromotionTypes.Campaign, quote.GetProperty("appliedPromotion").GetProperty("promotionType").GetString());
        Assert.Equal(0, store.BatchDiscountQueryCount);

        orderStore.TerritoryAllowed = false;
        var denied = await client.GetAsync(
            $"/api/v1/pricing/snapshot?productId={store.Product.Id}&quantity=10&retailerId={orderStore.Retailer.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Sales_capture_list_detail_and_timeline_share_scoped_read_model()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestOrderStore(identityFactory.Sales.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderStore>();
                services.AddSingleton<IOrderStore>(store);
                services.RemoveAll<IOrderPricingService>();
                services.AddSingleton<IOrderPricingService, TestPricingService>();
            }));
        using var client = factory.CreateClient();
        var request = new
        {
            clientOrderKey = Guid.NewGuid(),
            retailerId = store.Retailer.Id,
            paymentMethod = OrderPaymentMethods.Cod,
            requestedDeliveryDate = "2027-01-10",
            deliveryAddress = "12 Nguyễn Huệ, Thành phố Hồ Chí Minh",
            note = "Giao trong giờ hành chính",
            lines = new[] { new { productId = store.Product.Id, quantity = 10 } }
        };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/orders", request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.WarehouseKeeper, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/orders", request)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var orderId = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("finalPricePendingAllocation").GetBoolean());
        Assert.Empty(created.GetProperty("lines")[0].GetProperty("allocations").EnumerateArray());
        Assert.Equal(900m, created.GetProperty("finalAmount").GetDecimal());
        Assert.Equal("OrderCaptured", Assert.Single(store.Audits).Action);

        var list = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/orders?createdByMe=true&page=1&pageSize=10&search=DH-");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal("PendingRun", list.GetProperty("items")[0].GetProperty("allocationState").GetString());

        var timeline = await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}/timeline");
        Assert.Equal("PendingApproval", timeline.GetProperty("currentStatus").GetString());
        Assert.Single(timeline.GetProperty("events").EnumerateArray());

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($id: UUID!) { orders(createdByMe: true, page: 1, pageSize: 10) { totalCount items { id allocationState finalPricePendingAllocation } } orderTimeline(id: $id) { currentStatus events { toStatus } } }",
            variables = new { id = orderId }
        });
        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        var graphQl = JsonDocument.Parse(await graphQlResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.False(graphQl.TryGetProperty("errors", out _), graphQl.ToString());
        Assert.Equal(1, graphQl.GetProperty("data").GetProperty("orders").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Direct_order_create_is_denied_when_retailer_is_outside_sales_territory()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestOrderStore(identityFactory.Sales.Id) { TerritoryAllowed = false };
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderStore>();
                services.AddSingleton<IOrderStore>(store);
                services.RemoveAll<IOrderPricingService>();
                services.AddSingleton<IOrderPricingService, TestPricingService>();
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);

        var response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            clientOrderKey = Guid.NewGuid(),
            retailerId = store.Retailer.Id,
            paymentMethod = OrderPaymentMethods.Cod,
            requestedDeliveryDate = "2027-01-10",
            lines = new[] { new { productId = store.Product.Id, quantity = 10 } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(store.Orders);
    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestPricingService : IOrderPricingService
    {
        public Task<OrderPriceSnapshot> CalculateAsync(
            Guid productId, int quantity, Guid retailerId, DateTimeOffset pricedAt,
            CancellationToken cancellationToken)
        {
            var gross = quantity * 100m;
            return Task.FromResult(new OrderPriceSnapshot(100m, gross, 10m, gross * 0.1m,
                gross * 0.9m, Guid.Parse("40000000-0000-0000-0000-000000000001"),
                "Campaign", "KM-001"));
        }
    }

    private sealed class TestQuoteStore : IPromotionStore
    {
        private readonly Promotion campaign;
        public TestQuoteStore()
        {
            Product.ProductCategoryId = Category.Id;
            Price.ProductId = Product.Id;
            Batch.ProductId = Product.Id;
            campaign = new Promotion
            {
                Id = Guid.NewGuid(),
                Code = "KM-001",
                Name = "Khuyến mãi bán hàng",
                PromotionType = PromotionTypes.Campaign,
                MinimumQuantity = 1,
                DiscountPercent = 8,
                StartAt = DateTimeOffset.UtcNow.AddDays(-1),
                EndAt = DateTimeOffset.UtcNow.AddDays(1),
                IsActive = true
            };
        }

        public ProductCategory Category { get; } = new() { Id = Guid.NewGuid(), Code = "CAT-02", Name = "Sữa" };
        public Product Product { get; } = new()
        {
            Id = Guid.NewGuid(),
            Sku = "SKU001",
            Name = "Sữa tươi",
            Status = ProductStatuses.Published
        };
        public ProductPrice Price { get; } = new() { Id = Guid.NewGuid(), UnitPrice = 100m, IsActive = true };
        public Retailer Retailer { get; } = new() { Id = Guid.NewGuid(), Code = "10004127", Name = "Cửa hàng An Phú" };
        public Batch Batch { get; } = new() { Id = Guid.NewGuid(), BatchCode = "LO-HSD-001" };
        public int BatchDiscountQueryCount { get; private set; }

        public Task<PriceProductView?> FindPriceProductAsync(Guid productId, DateTimeOffset at,
            CancellationToken cancellationToken) => Task.FromResult<PriceProductView?>(
            productId == Product.Id ? new(Product, Price) : null);
        public Task<IReadOnlyCollection<PromotionView>> ListEligibleCampaignsAsync(
            Guid productId, Guid categoryId, Guid? retailerId, decimal quantity, DateTimeOffset at,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionView>>(
            [new(campaign, [Product.Id], [], [])]);
        public Task<IReadOnlyCollection<BatchDiscountView>> ListEligibleBatchDiscountsAsync(
            Guid batchId, decimal quantity, DateTimeOffset at, CancellationToken cancellationToken)
        {
            BatchDiscountQueryCount++;
            throw new InvalidOperationException("Indicative quote must not query batch discounts.");
        }
        public Task<Batch?> FindPriceBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<Batch?>(batchId == Batch.Id ? Batch : null);
        public Task<IReadOnlyCollection<PromotionView>> ListPromotionsAsync(bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PromotionView>>([]);
        public Task<PromotionView?> FindPromotionViewAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PromotionView?>(null);
        public Task<Promotion?> FindPromotionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Promotion?>(null);
        public Task<string> NextPromotionCodeAsync(CancellationToken cancellationToken) => Task.FromResult("KM-002");
        public Task<IReadOnlyCollection<Product>> FindProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<Product>>([]);
        public Task<IReadOnlyCollection<ProductCategory>> FindCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ProductCategory>>([]);
        public Task<IReadOnlyCollection<Retailer>> FindRetailersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<Retailer>>([]);
        public Task<IReadOnlyCollection<PromotionProduct>> ListPromotionProductsAsync(Guid promotionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionProduct>>([]);
        public Task<IReadOnlyCollection<PromotionCategory>> ListPromotionCategoriesAsync(Guid promotionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionCategory>>([]);
        public Task<IReadOnlyCollection<PromotionRetailer>> ListPromotionRetailersAsync(Guid promotionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionRetailer>>([]);
        public Task<IReadOnlyCollection<BatchDiscountCandidate>> ListBatchDiscountCandidatesAsync(Guid? warehouseId, Guid? productId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<BatchDiscountCandidate>>([]);
        public Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<Batch?>(null);
        public Task<BatchDiscountView?> FindBatchDiscountAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BatchDiscountView?>(null);
        public Task<IReadOnlyCollection<BatchDiscountView>> ListBatchDiscountsAsync(Guid? warehouseId, Guid? productId, bool includeInactive, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<BatchDiscountView>>([]);
        public Task<bool> HasOverlappingBatchDiscountAsync(Guid batchId, DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludingAssignmentId, CancellationToken cancellationToken) => Task.FromResult(false);
        public void AddPromotion(Promotion promotion) => throw new NotSupportedException();
        public void AddPromotionProduct(PromotionProduct link) => throw new NotSupportedException();
        public void AddPromotionCategory(PromotionCategory link) => throw new NotSupportedException();
        public void AddPromotionRetailer(PromotionRetailer link) => throw new NotSupportedException();
        public void AddBatchPromotion(BatchPromotion assignment) => throw new NotSupportedException();
        public void AddAuditLog(AuditLog auditLog) => throw new NotSupportedException();
        public void RemovePromotionProducts(IEnumerable<PromotionProduct> links) => throw new NotSupportedException();
        public void RemovePromotionCategories(IEnumerable<PromotionCategory> links) => throw new NotSupportedException();
        public void RemovePromotionRetailers(IEnumerable<PromotionRetailer> links) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestOrderStore(Guid salesUserId) : IOrderStore
    {
        public Retailer Retailer { get; } = new()
        {
            Id = Guid.NewGuid(),
            Code = "10004127",
            Name = "Cửa hàng An Phú",
            Address = "12 Nguyễn Huệ, Thành phố Hồ Chí Minh",
            Status = "Active",
            CreditTermDays = 14
        };
        public Product Product { get; } = new()
        {
            Id = Guid.NewGuid(),
            Sku = "SKU001",
            Name = "Sữa tươi 1 lít",
            Status = ProductStatuses.Published
        };
        public bool TerritoryAllowed { get; set; } = true;
        public List<SalesOrder> Orders { get; } = [];
        public List<SalesOrderLine> Lines { get; } = [];
        public List<OrderPromotion> Promotions { get; } = [];
        public List<OrderStatusHistory> History { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public Task<SalesOrder?> FindByClientOrderKeyAsync(Guid key, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.SingleOrDefault(item => item.ClientOrderKey == key));
        public Task<OrderRetailerContext?> FindRetailerContextAsync(Guid userId, Guid retailerId,
            CancellationToken cancellationToken) => Task.FromResult<OrderRetailerContext?>(
            TerritoryAllowed && userId == salesUserId && retailerId == Retailer.Id
                ? new(Retailer, Guid.Parse("20000000-0000-0000-0000-000000000001")) : null);
        public Task<Product?> FindPublishedProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<Product?>(productId == Product.Id ? Product : null);
        public Task<IReadOnlyCollection<OrderInventoryCandidate>> ListCandidatesAsync(Guid productId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Capture must not query stock.");
        public Task<MapLocation?> FindRetailerLocationAsync(Guid retailerId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Capture must not query allocation distance.");
        public Task<decimal?> FindMlorRatioAsync(Guid retailerId, Guid productId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Capture must not resolve allocation MLOR.");
        public Task<string?> FindConfigurationAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(key == "order.order_step_cartons" ? "1" : null);
        public Task<string> NextOrderCodeAsync(CancellationToken cancellationToken) => Task.FromResult("DH-001100");
        public Task<OrderView?> FindAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Orders.SingleOrDefault(item => item.Id == orderId)));
        public Task<OrderView?> FindByCodeAsync(string orderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Orders.SingleOrDefault(item => item.OrderCode == orderCode)));
        public Task<(IReadOnlyCollection<OrderView> Items, int TotalCount)> ListAsync(
            Guid actorUserId, string actorRole, OrderListQuery query, CancellationToken cancellationToken)
        {
            var items = Orders.AsEnumerable();
            if (actorRole == SystemRoles.Sales) items = items.Where(item => item.CreatedByUserId == actorUserId);
            if (query.CreatedByMe) items = items.Where(item => item.CreatedByUserId == actorUserId);
            if (!string.IsNullOrWhiteSpace(query.Status)) items = items.Where(item => item.Status == query.Status);
            if (!string.IsNullOrWhiteSpace(query.Search)) items = items.Where(item =>
                item.OrderCode.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            var views = items.Select(item => ToView(item)!).ToArray();
            return Task.FromResult<(IReadOnlyCollection<OrderView>, int)>((views, views.Length));
        }
        public Task<bool> CanViewAsync(OrderView order, Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) => Task.FromResult(
            actorRole is SystemRoles.Admin or SystemRoles.Operator ||
            actorRole == SystemRoles.Sales && order.Order.CreatedByUserId == actorUserId);
        public void AddOrder(SalesOrder order) => Orders.Add(order);
        public void AddLine(SalesOrderLine line) => Lines.Add(line);
        public void AddAllocation(Allocation allocation) => throw new InvalidOperationException("Capture must not allocate.");
        public void AddPromotion(OrderPromotion promotion) => Promotions.Add(promotion);
        public void AddHistory(OrderStatusHistory history) => History.Add(history);
        public void AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }

        private OrderView? ToView(SalesOrder? order)
        {
            if (order is null) return null;
            var lines = Lines.Where(item => item.SalesOrderId == order.Id)
                .Select(item => new OrderLineView(item, [])).ToArray();
            return new(order, lines, History.Where(item => item.SalesOrderId == order.Id).ToArray(), [],
                new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, int>());
        }
    }
}
