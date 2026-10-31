using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.MobileSnapshots;
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
    public async Task Mobile_snapshot_is_sales_only_and_sync_conflict_does_not_create_order()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var orderStore = new TestOrderStore(identityFactory.Sales.Id);
        var snapshotStore = new TestMobileSnapshotStore();
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderStore>();
                services.AddSingleton<IOrderStore>(orderStore);
                services.RemoveAll<IOrderPricingService>();
                services.AddSingleton<IOrderPricingService, TestPricingService>();
                services.RemoveAll<IMobileSnapshotStore>();
                services.AddSingleton<IMobileSnapshotStore>(snapshotStore);
            }));
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/mobile/snapshot")).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/v1/mobile/snapshot");
        Assert.Equal("Full", snapshot.GetProperty("deltaMode").GetString());
        Assert.Contains("tham khảo", snapshot.GetProperty("stockDisclaimer").GetString());
        var version = snapshot.GetProperty("version").GetString();
        using var cacheRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/mobile/snapshot");
        cacheRequest.Headers.IfNoneMatch.ParseAdd($"\"{version}\"");
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(cacheRequest)).StatusCode);

        var key = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/orders/sync", new
        {
            clientOrderKey = key,
            retailerId = orderStore.Retailer.Id,
            paymentMethod = OrderPaymentMethods.Prepaid,
            requestedDeliveryDate = "2027-01-10",
            offlineEstimatedAmount = 1_000m,
            snapshotVersion = snapshot.GetProperty("version").GetString(),
            expectedConditions = new[]
            {
                new
                {
                    productId = orderStore.Product.Id,
                    expectedBaseUnitPrice = 90m,
                    expectedMlorVersion = 2,
                    expectedMlorRatio = 0.25m
                }
            },
            lines = new[] { new { productId = orderStore.Product.Id, quantity = 10 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("details").GetProperty("requiresExplicitConfirmation").GetBoolean());
        Assert.Equal(900m, problem.GetProperty("details").GetProperty("currentIndicativeAmount").GetDecimal());
        Assert.Empty(orderStore.Orders);
    }

    [Fact]
    public async Task Pricing_snapshot_rest_and_graphql_share_the_guid_contract_without_batch_pricing()
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

        Assert.Equal(0, store.BatchDiscountQueryCount);

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = $$"""
                query {
                  entities {
                    pricingSnapshot(
                      productId: "{{store.Product.Id}}"
                      quantity: 10
                      retailerId: "{{orderStore.Retailer.Id}}") {
                      productId sku quantity unitPrice grossAmount discountPercent
                      discountAmount baseUnitPrice indicativeNetAmount
                      finalPricePendingAllocation batchId productPriceId
                      appliedPromotion { code minimumQuantity discountPercent }
                    }
                  }
                }
                """
        });
        var graphQlBody = await graphQlResponse.Content.ReadAsStringAsync();
        Assert.True(graphQlResponse.StatusCode == HttpStatusCode.OK, graphQlBody);
        var graphQl = JsonDocument.Parse(graphQlBody).RootElement;
        Assert.False(graphQl.TryGetProperty("errors", out _), graphQlBody);
        var graphQlSnapshot = graphQl.GetProperty("data").GetProperty("entities")
            .GetProperty("pricingSnapshot");
        Assert.Equal(store.Product.Id, graphQlSnapshot.GetProperty("productId").GetGuid());
        Assert.True(graphQlSnapshot.GetProperty("finalPricePendingAllocation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, graphQlSnapshot.GetProperty("batchId").ValueKind);
        Assert.Equal(rest.GetProperty("productPriceId").GetGuid(),
            graphQlSnapshot.GetProperty("productPriceId").GetGuid());
        Assert.Equal(rest.GetProperty("indicativeNetAmount").GetDecimal(),
            graphQlSnapshot.GetProperty("indicativeNetAmount").GetDecimal());

        orderStore.TerritoryAllowed = false;
        var denied = await client.GetAsync(
            $"/api/v1/pricing/snapshot?productId={store.Product.Id}&quantity=10&retailerId={orderStore.Retailer.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var deniedProblem = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("forbidden", deniedProblem.GetProperty("code").GetString());
        Assert.Contains("assigned territory", deniedProblem.GetProperty("title").GetString());
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
            paymentMethod = OrderPaymentMethods.Prepaid,
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
        var createdLine = created.GetProperty("lines")[0];
        Assert.Empty(createdLine.GetProperty("allocations").EnumerateArray());
        Assert.Equal("Sku", createdLine.GetProperty("mlor").GetProperty("sourceScope").GetString());
        Assert.Equal(2, createdLine.GetProperty("mlor").GetProperty("sourceVersion").GetInt32());
        Assert.Equal("KM-001", createdLine.GetProperty("ordinaryPromotion").GetProperty("code").GetString());
        Assert.NotEqual(Guid.Empty, createdLine.GetProperty("productPriceId").GetGuid());
        Assert.Equal(900m, created.GetProperty("finalAmount").GetDecimal());
        Assert.Equal("OrderCaptured", Assert.Single(store.Audits).Action);

        var originalPromotion = Assert.Single(store.Promotions);
        store.Promotions.Add(new OrderPromotion
        {
            Id = Guid.NewGuid(),
            SalesOrderLineId = originalPromotion.SalesOrderLineId,
            PromotionId = Guid.NewGuid(),
            PromotionCodeSnapshot = "KM-LEGACY",
            PromotionNameSnapshot = "Khuyến mãi dữ liệu cũ",
            PromotionTypeSnapshot = "Campaign",
            MinimumQuantitySnapshot = 1m,
            EffectiveFromSnapshot = originalPromotion.EffectiveFromSnapshot,
            EffectiveToSnapshot = originalPromotion.EffectiveToSnapshot,
            DiscountPercent = 5m,
            DiscountAmount = 50m,
            IsServerFinal = false,
            AppliedAt = originalPromotion.AppliedAt.AddMinutes(-1)
        });

        var list = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/orders?createdByMe=true&page=1&pageSize=10&search=DH-");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal("PendingRun", list.GetProperty("items")[0].GetProperty("allocationState").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}");
        Assert.Equal("KM-001", detail.GetProperty("lines")[0]
            .GetProperty("ordinaryPromotion").GetProperty("code").GetString());

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = $$"""
                query {
                  entities {
                    orderRead(id: "{{orderId}}") {
                      id orderCode allocationState officialAmount
                      lines { id allocationFragments { batchId finalUnitPrice netAmount } }
                    }
                    orderReads(createdByMe: true, search: "DH-") {
                      totalCount items { id orderCode }
                    }
                  }
                }
                """
        });
        var graphQlBody = await graphQlResponse.Content.ReadAsStringAsync();
        Assert.True(graphQlResponse.IsSuccessStatusCode, graphQlBody);
        var graphQl = JsonDocument.Parse(graphQlBody).RootElement;
        Assert.False(graphQl.TryGetProperty("errors", out _), graphQlBody);
        Assert.Equal(orderId, graphQl.GetProperty("data").GetProperty("entities")
            .GetProperty("orderRead").GetProperty("id").GetGuid());

        var timeline = await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}/timeline");
        Assert.Equal("PendingApproval", timeline.GetProperty("currentStatus").GetString());
        Assert.Single(timeline.GetProperty("events").EnumerateArray());

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
            paymentMethod = OrderPaymentMethods.Prepaid,
            requestedDeliveryDate = "2027-01-10",
            lines = new[] { new { productId = store.Product.Id, quantity = 10 } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("forbidden", problem.GetProperty("code").GetString());
        Assert.Contains("assigned territory", problem.GetProperty("title").GetString());
        Assert.Empty(store.Orders);
    }

    [Fact]
    public async Task Direct_and_synced_order_contracts_require_resource_guids_and_reject_inactive_retailers()
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
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);

        var businessCodePayload = new
        {
            clientOrderKey = Guid.NewGuid(),
            retailerId = store.Retailer.Code,
            paymentMethod = OrderPaymentMethods.Prepaid,
            requestedDeliveryDate = "2027-01-10",
            lines = new[] { new { productId = store.Product.Sku, quantity = 10 } }
        };
        var invalidGuid = await client.PostAsJsonAsync("/api/v1/orders", businessCodePayload);
        Assert.Equal(HttpStatusCode.BadRequest, invalidGuid.StatusCode);
        Assert.Empty(store.Orders);

        store.Retailer.Status = "Inactive";
        var canonicalPayload = new
        {
            clientOrderKey = Guid.NewGuid(),
            retailerId = store.Retailer.Id,
            paymentMethod = OrderPaymentMethods.Prepaid,
            requestedDeliveryDate = "2027-01-10",
            lines = new[] { new { productId = store.Product.Id, quantity = 10 } }
        };
        var inactive = await client.PostAsJsonAsync("/api/v1/orders", canonicalPayload);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        var inactiveProblem = await inactive.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("conflict", inactiveProblem.GetProperty("code").GetString());
        Assert.Contains("not active", inactiveProblem.GetProperty("title").GetString());
        Assert.Empty(store.Orders);

        store.Retailer.Status = "Active";
        var syncPayload = new
        {
            clientOrderKey = Guid.NewGuid(),
            retailerId = store.Retailer.Id,
            paymentMethod = OrderPaymentMethods.Prepaid,
            requestedDeliveryDate = "2027-01-10",
            offlineEstimatedAmount = 900m,
            snapshotVersion = "mobile-contract-v1",
            confirmConditionChanges = false,
            expectedConditions = new[]
            {
                new
                {
                    productId = store.Product.Id,
                    expectedBaseUnitPrice = 90m,
                    expectedMlorVersion = 2,
                    expectedMlorRatio = 0.25m
                }
            },
            lines = new[] { new { productId = store.Product.Id, quantity = 10 } }
        };
        var synced = await client.PostAsJsonAsync("/api/v1/orders/sync", syncPayload);
        Assert.Equal(HttpStatusCode.Created, synced.StatusCode);
        var syncedOrder = await synced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(store.Retailer.Id, syncedOrder.GetProperty("retailerId").GetGuid());
        Assert.Equal(OrderPaymentMethods.Prepaid,
            syncedOrder.GetProperty("paymentMethod").GetString());
        Assert.Equal(OrderStatuses.PendingApproval, syncedOrder.GetProperty("status").GetString());
        Assert.True(syncedOrder.GetProperty("finalPricePendingAllocation").GetBoolean());
        Assert.Equal(900m, syncedOrder.GetProperty("finalAmount").GetDecimal());
        Assert.Empty(syncedOrder.GetProperty("lines")[0].GetProperty("allocations").EnumerateArray());
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
                gross * 0.9m, Guid.Parse("50000000-0000-0000-0000-000000000001"),
                pricedAt.AddDays(-30), null,
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                "Campaign", "KM-001", "Khuyến mãi tháng mười", 5m,
                pricedAt.AddDays(-1), pricedAt.AddDays(10)));
        }
    }

    private sealed class TestMobileSnapshotStore : IMobileSnapshotStore
    {
        public Task<MobileSnapshotData> LoadAsync(Guid salesUserId, DateTimeOffset effectiveAt,
            CancellationToken cancellationToken) => Task.FromResult(new MobileSnapshotData(
                [], [], [], [], [], [], [], null));
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
        public Task<OrderMlorSnapshot?> ResolveMlorAsync(
            Guid retailerId, Guid productId, DateTimeOffset at,
            CancellationToken cancellationToken) => Task.FromResult<OrderMlorSnapshot?>(new(
                "Sku", Guid.Parse("30000000-0000-0000-0000-000000000001"), 2,
                null, 0.25m, null, at));
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
                .Select(item => new OrderLineView(item, [],
                    Promotions.Where(promotion => promotion.SalesOrderLineId == item.Id)
                        .OrderByDescending(promotion => promotion.IsServerFinal)
                        .ThenByDescending(promotion => promotion.AppliedAt)
                        .ThenBy(promotion => promotion.Id)
                        .FirstOrDefault())).ToArray();
            return new(order, lines, History.Where(item => item.SalesOrderId == order.Id).ToArray(), [],
                new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), new Dictionary<Guid, int>());
        }
    }
}
