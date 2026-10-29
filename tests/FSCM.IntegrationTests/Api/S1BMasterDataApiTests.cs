using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.Maps;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.TransitTables;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class S1BMasterDataApiTests
{
    [Fact]
    public async Task Warehouse_legacy_transit_api_is_removed_and_eta_uses_shared_table()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var warehouseStore = new TestWarehouseStore(
            identityFactory.Admin.Id, identityFactory.WarehouseKeeper);
        var mapStore = new TestMapStore(warehouseStore);
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWarehouseConfigurationStore>();
                services.AddSingleton<IWarehouseConfigurationStore>(warehouseStore);
                services.RemoveAll<IMapStore>();
                services.AddSingleton<IMapStore>(mapStore);
                services.RemoveAll<IDeliveryEtaStore>();
                services.AddSingleton<IDeliveryEtaStore>(mapStore);
                services.RemoveAll<ITransitTableStore>();
                services.AddSingleton<ITransitTableStore>(new TestTransitTableStore());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestMapStore.Now));
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);

        var warehouse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/warehouses/{warehouseStore.Warehouse.Id}");
        Assert.Equal(120, warehouse.GetProperty("minimumStockThresholdCartons").GetInt32());
        var keeper = Assert.Single(warehouse.GetProperty("keepers").EnumerateArray());
        Assert.Equal(identityFactory.WarehouseKeeper.Id, keeper.GetProperty("userId").GetGuid());

        var warehouseGraphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($id: UUID!) { entities { warehouseProfile(warehouseId: $id) { id code minimumStockThresholdCartons keepers { userId fullName } } } }",
            variables = new { id = warehouseStore.Warehouse.Id }
        });
        var warehouseGraphQlBody = await warehouseGraphQlResponse.Content.ReadAsStringAsync();
        Assert.True(warehouseGraphQlResponse.IsSuccessStatusCode, warehouseGraphQlBody);
        var warehouseGraphQl = JsonDocument.Parse(warehouseGraphQlBody).RootElement;
        Assert.False(warehouseGraphQl.TryGetProperty("errors", out _), warehouseGraphQlBody);
        Assert.Equal(identityFactory.WarehouseKeeper.Id, warehouseGraphQl.GetProperty("data")
            .GetProperty("entities").GetProperty("warehouseProfile")
            .GetProperty("keepers")[0].GetProperty("userId").GetGuid());

        var legacyRoute = $"/api/v1/warehouses/{warehouseStore.Warehouse.Id}/transit-time-rules";
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync(legacyRoute)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await client.GetAsync($"{legacyRoute}/history")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync(legacyRoute, new { rules = Array.Empty<object>() })).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/v1/mlor-configurations/{warehouseStore.Mlor.Id}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/mlor-configurations/{warehouseStore.Mlor.Id}")).StatusCode);
        Assert.False(warehouseStore.Mlor.IsActive);
        Assert.Equal("RetailerMlorRuleDeactivated", Assert.Single(warehouseStore.AuditLogs).Action);

        var etaResponse = await client.PostAsJsonAsync("/api/v1/eta/estimate", new
        {
            warehouseId = warehouseStore.Warehouse.Id,
            retailerId = mapStore.RetailerId,
            referenceAt = TestMapStore.Now
        });
        etaResponse.EnsureSuccessStatusCode();
        var eta = JsonDocument.Parse(await etaResponse.Content.ReadAsStringAsync()).RootElement;
        var breakdown = eta.GetProperty("breakdown");
        Assert.Equal(240, breakdown.GetProperty("waitForRunMinutes").GetInt32());
        Assert.Equal(660, breakdown.GetProperty("waitForDispatchSlotMinutes").GetInt32());
        Assert.Equal(180_000m, eta.GetProperty("estimatedCostAmount").GetDecimal());

        var removedGraphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($id: UUID!) { warehouse(id: $id) { transitTimeRules { version } } transitTimeRules(warehouseId: $id) { nodes { version } } }",
            variables = new { id = warehouseStore.Warehouse.Id }
        });
        var removedGraphQl = JsonDocument.Parse(
            await removedGraphQlResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(removedGraphQl.TryGetProperty("errors", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task Retailer_rest_and_graphql_return_general_information_mlor_and_sku_precedence()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestRetailerStore(identityFactory.Admin.Id, identityFactory.Sales.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRetailerStore>();
                services.AddSingleton<IRetailerStore>(store);
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/retailers/{store.Retailer.Id}");
        Assert.Equal("Cửa hàng An Phú", detail.GetProperty("generalInformation").GetProperty("name").GetString());
        Assert.Equal(2, detail.GetProperty("mlorDeclarations").GetArrayLength());

        var resolution = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/retailers/{store.Retailer.Id}/mlor-resolution?productId={store.Product.Id}");
        Assert.Equal("Sku", resolution.GetProperty("sourceScope").GetString());
        Assert.Equal(0.55m, resolution.GetProperty("minimumLifeRatio").GetDecimal());

        var retailerGraphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($retailerId: UUID!, $productId: UUID!) { entities { retailerProfile(retailerId: $retailerId) { id generalInformation { name } mlorDeclarations { scope productId minimumLifeRatio } } retailerMlorResolution(retailerId: $retailerId, productId: $productId) { sourceScope minimumLifeRatio } } }",
            variables = new { retailerId = store.Retailer.Id, productId = store.Product.Id }
        });
        var retailerGraphQlBody = await retailerGraphQlResponse.Content.ReadAsStringAsync();
        Assert.True(retailerGraphQlResponse.IsSuccessStatusCode, retailerGraphQlBody);
        var retailerGraphQl = JsonDocument.Parse(retailerGraphQlBody).RootElement;
        Assert.False(retailerGraphQl.TryGetProperty("errors", out _), retailerGraphQlBody);
        var retailerProfile = retailerGraphQl.GetProperty("data").GetProperty("entities");
        Assert.Equal("Cửa hàng An Phú", retailerProfile.GetProperty("retailerProfile")
            .GetProperty("generalInformation").GetProperty("name").GetString());
        Assert.Equal("Sku", retailerProfile.GetProperty("retailerMlorResolution")
            .GetProperty("sourceScope").GetString());

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/mlor-configurations?retailerId={store.Retailer.Id}")).StatusCode);
        var otherSales = identityFactory.AddTestUser("sales.other@test.local", FSCM.Domain.Authorization.SystemRoles.Sales);
        client.DefaultRequestHeaders.Authorization = Bearer(otherSales, identityFactory);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/retailers?scope=mine"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/retailers/{store.Retailer.Id}")).StatusCode);

        store.Retailer.Status = RetailerStatuses.PendingApproval;
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        var review = await client.PostAsJsonAsync($"/api/v1/retailers/{store.Retailer.Id}/review", new
        {
            approve = true,
            creditTermDays = 14,
            reason = "Hồ sơ và MLOR hợp lệ"
        });
        review.EnsureSuccessStatusCode();
        var reviewed = JsonDocument.Parse(await review.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(RetailerStatuses.Active, reviewed.GetProperty("status").GetString());
        Assert.Equal("10005000", reviewed.GetProperty("code").GetString());

    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestWarehouseStore : IWarehouseConfigurationStore
    {
        public TestWarehouseStore(Guid adminId, UserAccount warehouseKeeper)
        {
            AdminId = adminId;
            Warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                Code = "WH-TD",
                Name = "Kho Thủ Đức",
                Type = "Regional",
                Address = "Thành phố Thủ Đức",
                MinimumStockThresholdCartons = 120,
                IsActive = true,
                CreatedAt = TestMapStore.Now,
                UpdatedAt = TestMapStore.Now
            };
            Location = new MapLocation
            {
                Id = Guid.NewGuid(),
                WarehouseId = Warehouse.Id,
                Latitude = 10.8m,
                Longitude = 106.7m,
                Provider = "Manual",
                Version = 1,
                CreatedAt = TestMapStore.Now,
                UpdatedAt = TestMapStore.Now
            };
            Rules.Add(new TransitTimeRule
            {
                Id = Guid.NewGuid(),
                WarehouseId = Warehouse.Id,
                MinDistanceMeters = 0,
                MaxDistanceMeters = 20_000,
                HandlingMinutes = 60,
                TransitMinutes = 120,
                EstimatedTransportCost = 180_000m,
                MaxServingDistanceMeters = 20_000,
                Version = 1,
                EffectiveFrom = TestMapStore.Now,
                IsActive = true,
                CreatedAt = TestMapStore.Now,
                UpdatedAt = TestMapStore.Now
            });
            Keeper = new WarehouseKeeperView(
                Warehouse.Id, warehouseKeeper.Id, warehouseKeeper.FullName, warehouseKeeper.Email);
            Mlor = new RetailerProductMlor
            {
                Id = Guid.NewGuid(),
                RetailerId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                MinimumLifeRatio = 0.3m,
                Version = 1,
                UpdatedByUserId = adminId,
                EffectiveFrom = TestMapStore.Now,
                IsActive = true
            };
        }

        public Guid AdminId { get; }
        public Warehouse Warehouse { get; }
        public MapLocation Location { get; }
        public List<TransitTimeRule> Rules { get; } = [];
        public WarehouseKeeperView Keeper { get; }
        public RetailerProductMlor Mlor { get; }
        public List<AuditLog> AuditLogs { get; } = [];
        public Task<IReadOnlyCollection<WarehouseView>> ListWarehousesAsync(bool includeInactive, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<WarehouseView>>([new(Warehouse, Location)]);
        public Task<WarehouseView?> FindWarehouseViewAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult<WarehouseView?>(warehouseId == Warehouse.Id ? new(Warehouse, Location) : null);
        public Task<Warehouse?> FindWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult<Warehouse?>(warehouseId == Warehouse.Id ? Warehouse : null);
        public Task<Warehouse?> FindWarehouseByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult<Warehouse?>(code == Warehouse.Code ? Warehouse : null);
        public Task<MapLocation?> FindWarehouseLocationAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult<MapLocation?>(warehouseId == Warehouse.Id ? Location : null);
        public Task<IReadOnlyCollection<WarehouseKeeperView>> ListActiveWarehouseKeepersAsync(
            IReadOnlyCollection<Guid> warehouseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<WarehouseKeeperView>>(
                warehouseIds.Contains(Warehouse.Id) ? [Keeper] : []);
        public void AddWarehouse(Warehouse warehouse) { }
        public void AddLocation(MapLocation location) { }
        public Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(Guid warehouseId, bool includeInactive, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<TransitTimeRule>>(Rules.Where(item => includeInactive || item.IsActive).ToArray());
        public Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(IReadOnlyCollection<Guid> warehouseIds, bool includeInactive, CancellationToken cancellationToken) => ListTransitRulesAsync(Warehouse.Id, includeInactive, cancellationToken);
        public void AddTransitRules(IEnumerable<TransitTimeRule> rules) => Rules.AddRange(rules);
        public Task<IReadOnlyCollection<MlorView>> ListMlorAsync(Guid? retailerId, Guid? productCategoryId, Guid? productId, bool includeInactive, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<MlorView>>([]);
        public Task<RetailerProductMlor?> FindActiveMlorAsync(Guid retailerId, Guid? productCategoryId, Guid? productId, CancellationToken cancellationToken) => Task.FromResult<RetailerProductMlor?>(null);
        public Task<RetailerProductMlor?> FindMlorAsync(Guid mlorId, CancellationToken cancellationToken) =>
            Task.FromResult<RetailerProductMlor?>(mlorId == Mlor.Id ? Mlor : null);
        public Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<Retailer?>(null);
        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult<Product?>(null);
        public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) => Task.FromResult<ProductCategory?>(null);
        public void AddMlor(RetailerProductMlor mlor) { }
        public void AddAuditLog(AuditLog auditLog) => AuditLogs.Add(auditLog);
        public Task<IReadOnlyCollection<SystemConfiguration>> ListCurrentConfigurationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SystemConfiguration>>([]);
        public Task<IReadOnlyCollection<SystemConfiguration>> ListConfigurationHistoryAsync(string key, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SystemConfiguration>>([]);
        public Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken) => Task.FromResult<SystemConfiguration?>(null);
        public void AddConfiguration(SystemConfiguration configuration) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestMapStore(TestWarehouseStore warehouseStore) : IMapStore, IDeliveryEtaStore
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);
        public Guid RetailerId { get; } = Guid.NewGuid();
        private MapLocation RetailerLocation => new()
        {
            Id = Guid.Parse("c0000000-0000-0000-0000-000000000002"),
            RetailerId = RetailerId,
            Latitude = 10.7m,
            Longitude = 106.8m,
            Provider = "Manual",
            Version = 1,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult(warehouseId == warehouseStore.Warehouse.Id);
        public Task<bool> RetailerExistsAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult(retailerId == RetailerId);
        public Task<MapLocation?> FindLocationAsync(string entityType, Guid entityId, bool tracking, CancellationToken cancellationToken) => Task.FromResult<MapLocation?>(entityType == "warehouse" ? warehouseStore.Location : RetailerLocation);
        public Task<MapDistanceCache?> FindActiveDistanceAsync(Guid warehouseId, Guid retailerId, bool tracking, CancellationToken cancellationToken) => Task.FromResult<MapDistanceCache?>(new()
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            RetailerId = retailerId,
            OriginLocationId = warehouseStore.Location.Id,
            DestinationLocationId = RetailerLocation.Id,
            OriginLocationVersion = 1,
            DestinationLocationVersion = 1,
            DistanceMeters = 12_000,
            DurationSeconds = 1_800,
            Provider = "GoogleMaps",
            ProviderStatus = "ROUTE_EXISTS",
            Version = 2,
            CalculatedAt = Now.AddHours(-1),
            ExpiresAt = Now.AddHours(23),
            IsActive = true
        });
        public Task<IReadOnlyCollection<MapDistanceCache>> ListDistancesAsync(Guid? warehouseId, Guid? retailerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<MapDistanceCache>>([]);
        public Task<SystemConfiguration?> FindScheduleConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SystemConfiguration?>(new()
            {
                ConfigKey = "eta.schedule",
                ConfigValue = "{\"timeZone\":\"Asia/Ho_Chi_Minh\",\"allocationRunLocalTime\":\"12:00\"," +
                    "\"planApprovalDeadlineLocalTime\":\"14:00\",\"preparationMinutes\":240," +
                    "\"bufferMinutes\":60,\"dispatchSlotsLocalTime\":[\"06:00\",\"14:00\"]}",
                Version = 3,
                IsActive = true
            });
        public Task<MapDistanceCache?> FindDistanceAsync(Guid warehouseId, Guid retailerId, CancellationToken cancellationToken) =>
            FindActiveDistanceAsync(warehouseId, retailerId, false, cancellationToken);
        public Task<MapLocation?> FindLocationAsync(string entityType, Guid entityId, CancellationToken cancellationToken) =>
            FindLocationAsync(entityType, entityId, false, cancellationToken);
        public void AddLocation(MapLocation location) { }
        public void AddDistance(MapDistanceCache distance) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestTransitTableStore : ITransitTableStore
    {
        private readonly TransitTable _table = new()
        {
            Id = Guid.NewGuid(),
            Version = 1,
            MaxServingDistanceMeters = 20_000,
            EffectiveFrom = TestMapStore.Now.AddDays(-1),
            IsActive = true
        };

        private TransitTableView View => new(_table,
        [
            new TransitTableBand
            {
                TransitTableId = _table.Id,
                MinDistanceMeters = 0,
                MaxDistanceMeters = 20_000,
                HandlingMinutes = 60,
                TransitMinutes = 120,
                EstimatedCostAmount = 180_000m
            }
        ]);

        public Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken) => Task.FromResult<TransitTableView?>(View);
        public Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) => Task.FromResult<TransitTableView?>(version == 1 ? View : null);
        public Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) => Task.FromResult<TransitTableView?>(View);
        public Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<TransitTableView>>([View]);
        public Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) => Task.FromResult<TransitTable?>(_table);
        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public void AddTable(TransitTable table) { }
        public void AddBands(IEnumerable<TransitTableBand> bands) { }
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestRetailerStore : IRetailerStore
    {
        public TestRetailerStore(Guid adminId, Guid salesId)
        {
            SalesId = salesId;
            Retailer = new Retailer
            {
                Id = Guid.NewGuid(),
                RegionId = Guid.NewGuid(),
                Code = "10004127",
                Name = "Cửa hàng An Phú",
                Address = "Thành phố Hồ Chí Minh",
                Status = RetailerStatuses.Active,
                RetailerType = RetailerTypes.GroceryStore,
                StoreSizeSquareMeters = 40,
                StoreSizeCategory = "M",
                PurchaseFrequencyPerMonth = 4,
                CreditTermDays = 14,
                DeclaredByUserId = salesId,
                CreatedAt = TestMapStore.Now,
                UpdatedAt = TestMapStore.Now
            };
            Category = new ProductCategory { Id = Guid.NewGuid(), Code = "CAT-02", Name = "Sữa", IsActive = true };
            Product = new Product { Id = Guid.NewGuid(), ProductCategoryId = Category.Id, Sku = "SKU001", Name = "Sữa tươi", Status = "Published", ShelfLifeHours = 168 };
            Mlors.Add(new RetailerProductMlor { Id = Guid.NewGuid(), RetailerId = Retailer.Id, ProductCategoryId = Category.Id, MinimumLifeRatio = 0.3m, Version = 1, UpdatedByUserId = salesId, EffectiveFrom = TestMapStore.Now, IsActive = true });
            Mlors.Add(new RetailerProductMlor { Id = Guid.NewGuid(), RetailerId = Retailer.Id, ProductId = Product.Id, MinimumLifeRatio = 0.55m, Version = 2, UpdatedByUserId = adminId, EffectiveFrom = TestMapStore.Now, IsActive = true });
        }

        public Retailer Retailer { get; }
        public Guid SalesId { get; }
        public ProductCategory Category { get; }
        public Product Product { get; }
        public List<RetailerProductMlor> Mlors { get; } = [];
        public Task<IReadOnlyCollection<RetailerView>> ListAsync(Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RetailerView>>(
                actorRole == FSCM.Domain.Authorization.SystemRoles.Sales && actorUserId != SalesId
                    ? []
                    : [new(Retailer, Guid.NewGuid(), "HCM-A", null)]);
        public Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<RetailerView?>(new(Retailer, Guid.NewGuid(), "HCM-A", null));
        public Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<Retailer?>(Retailer);
        public Task<Retailer?> FindDuplicateAsync(
            string name, string? phone, string? email, string? address,
            Guid? excludedRetailerId, CancellationToken cancellationToken) => Task.FromResult<Retailer?>(null);
        public Task<IReadOnlyCollection<RetailerMlorView>> ListMlorDeclarationsAsync(IReadOnlyCollection<Guid> retailerIds, bool includeInactive, bool tracking, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<RetailerMlorView>>(Mlors.Where(item => includeInactive || item.IsActive).Select(item => new RetailerMlorView(item, item.ProductCategoryId.HasValue ? Category.Code : null, item.ProductCategoryId.HasValue ? Category.Name : null, item.ProductId.HasValue ? Product.Sku : null, item.ProductId.HasValue ? Product.Name : null)).ToArray());
        public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) => Task.FromResult<ProductCategory?>(Category);
        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult<Product?>(Product);
        public Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ProductCategory>>([Category]);
        public Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken) => Task.FromResult<SystemConfiguration?>(null);
        public Task<MapLocation?> FindLocationForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<MapLocation?>(null);
        public Task<bool> CanDeclareForGroupAsync(Guid userId, Guid regionId, Guid salesGroupId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<RetailerTerritoryAccess?> FindTerritoryAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<RetailerTerritoryAccess?>(null);
        public Task<bool> IsActiveSalesUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string> NextDeclarationCodeAsync(CancellationToken cancellationToken) => Task.FromResult("KB-0100");
        public Task<string> NextBusinessCodeAsync(CancellationToken cancellationToken) => Task.FromResult("10005000");
        public void Add(Retailer retailer) { }
        public void AddAssignment(RetailerSalesAssignment assignment) { }
        public void AddLocation(MapLocation location) { }
        public void AddMlor(RetailerProductMlor mlor) => Mlors.Add(mlor);
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
