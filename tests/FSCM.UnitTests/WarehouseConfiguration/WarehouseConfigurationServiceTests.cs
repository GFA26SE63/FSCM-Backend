using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.WarehouseConfiguration;

public sealed class WarehouseConfigurationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_warehouse_persists_type_address_and_coordinates()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateWarehouseAsync(new SaveWarehouseData(
            "wh-td", "Kho vùng Thủ Đức", WarehouseTypes.Regional,
            "Thành phố Thủ Đức, Thành phố Hồ Chí Minh", 10.848m, 106.772m,
            "dia-diem-thu-duc", 120),
            CancellationToken.None);

        Assert.Equal("WH-TD", result.Code);
        Assert.Equal(WarehouseTypes.Regional, result.Type);
        Assert.Equal(10.848m, result.Latitude);
        Assert.Equal(120, result.MinimumStockThresholdCartons);
        Assert.Single(fixture.Store.Warehouses);
        Assert.Single(fixture.Store.Locations);
    }

    [Fact]
    public async Task Transit_rules_reject_overlapping_distance_ranges()
    {
        var fixture = new Fixture();
        var warehouse = fixture.AddWarehouse();

        var exception = await Assert.ThrowsAsync<WarehouseConfigurationOperationException>(() =>
            fixture.Service.ReplaceTransitRulesAsync(warehouse.Id,
            [
                new TransitTimeRuleData(0, 10_000, 60, 1_440, 150_000m, 20_000),
                new TransitTimeRuleData(10_000, 20_000, 60, 2_880, 250_000m, 20_000)
            ], CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.TransitRules);
    }

    [Fact]
    public async Task Transit_rule_replacement_versions_history_and_identical_retry_is_idempotent()
    {
        var fixture = new Fixture();
        var warehouse = fixture.AddWarehouse();
        var definitions = new[] { new TransitTimeRuleData(0, 20_000, 60, 120, 180_000m, 20_000) };

        var first = await fixture.Service.ReplaceTransitRulesAsync(warehouse.Id, definitions, CancellationToken.None);
        var retry = await fixture.Service.ReplaceTransitRulesAsync(warehouse.Id, definitions, CancellationToken.None);

        Assert.Equal(1, first.Single().Version);
        Assert.Equal(first.Single().Id, retry.Single().Id);
        Assert.Single(fixture.Store.TransitRules);
    }

    [Fact]
    public async Task Setting_mlor_closes_previous_rule_and_keeps_history()
    {
        var fixture = new Fixture();
        var retailer = fixture.AddRetailer();
        var product = fixture.AddProduct();
        var previous = fixture.AddMlor(retailer.Id, product.Id, 0.2m);

        var result = await fixture.Service.SetMlorAsync(
            retailer.Id, null, product.Id, 0.3m, 48, Guid.NewGuid(), CancellationToken.None);

        Assert.False(previous.IsActive);
        Assert.Equal(Now, previous.EffectiveTo);
        Assert.Equal(0.3m, result.MinimumLifeRatio);
        Assert.Equal(2, fixture.Store.Mlors.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Mlor_ratio_must_be_strictly_between_zero_and_one(decimal ratio)
    {
        var fixture = new Fixture();
        var exception = await Assert.ThrowsAsync<WarehouseConfigurationOperationException>(() =>
            fixture.Service.SetMlorAsync(fixture.AddRetailer().Id, null, fixture.AddProduct().Id,
                ratio, null, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Configuration_change_creates_new_version_and_deactivates_previous()
    {
        var fixture = new Fixture();
        var userId = Guid.NewGuid();

        var first = await fixture.Service.UpdateConfigurationAsync(
            BusinessConfigurationKeys.ServiceRadiusMeters, "20000", userId, CancellationToken.None);
        var second = await fixture.Service.UpdateConfigurationAsync(
            BusinessConfigurationKeys.ServiceRadiusMeters, "25000", userId, CancellationToken.None);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.False(fixture.Store.Configurations.Single(item => item.Version == 1).IsActive);
        Assert.True(fixture.Store.Configurations.Single(item => item.Version == 2).IsActive);
    }

    [Fact]
    public async Task Allocation_weights_must_sum_to_one()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<WarehouseConfigurationOperationException>(() =>
            fixture.Service.UpdateConfigurationAsync(BusinessConfigurationKeys.AllocationWeights,
                "{\"expiry\":0.5,\"distance\":0.4,\"coverage\":0.2}", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Configurations);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new InMemoryStore();
            Service = new WarehouseConfigurationService(Store, new TestTimeProvider(Now));
        }

        public InMemoryStore Store { get; }
        public WarehouseConfigurationService Service { get; }

        public Warehouse AddWarehouse()
        {
            var warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                Code = "WH-CC",
                Name = "Kho trung tâm Củ Chi",
                Type = WarehouseTypes.Central,
                Address = "Huyện Củ Chi, Thành phố Hồ Chí Minh",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Store.Warehouses.Add(warehouse);
            return warehouse;
        }

        public Retailer AddRetailer()
        {
            var retailer = new Retailer { Id = Guid.NewGuid(), Code = "10004127", Name = "Cửa hàng An Phú" };
            Store.Retailers.Add(retailer);
            return retailer;
        }

        public Product AddProduct()
        {
            var product = new Product { Id = Guid.NewGuid(), Sku = "SKU001", Name = "Sữa tươi", ShelfLifeHours = 168 };
            Store.Products.Add(product);
            return product;
        }

        public RetailerProductMlor AddMlor(Guid retailerId, Guid productId, decimal ratio)
        {
            var mlor = new RetailerProductMlor
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                ProductId = productId,
                MinimumLifeRatio = ratio,
                EffectiveFrom = Now.AddDays(-10),
                IsActive = true
            };
            Store.Mlors.Add(mlor);
            return mlor;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryStore : IWarehouseConfigurationStore
    {
        public List<Warehouse> Warehouses { get; } = [];
        public List<MapLocation> Locations { get; } = [];
        public List<TransitTimeRule> TransitRules { get; } = [];
        public List<RetailerProductMlor> Mlors { get; } = [];
        public List<Retailer> Retailers { get; } = [];
        public List<Product> Products { get; } = [];
        public List<ProductCategory> Categories { get; } = [];
        public List<SystemConfiguration> Configurations { get; } = [];
        public List<AuditLog> AuditLogs { get; } = [];

        public Task<IReadOnlyCollection<WarehouseView>> ListWarehousesAsync(bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<WarehouseView>>(Warehouses.Where(item => includeInactive || item.IsActive)
                .Select(item => new WarehouseView(item, Locations.SingleOrDefault(location => location.WarehouseId == item.Id))).ToArray());
        public Task<WarehouseView?> FindWarehouseViewAsync(Guid id, CancellationToken cancellationToken)
        {
            var item = Warehouses.SingleOrDefault(candidate => candidate.Id == id);
            return Task.FromResult(item is null ? null : new WarehouseView(item, Locations.SingleOrDefault(location => location.WarehouseId == id)));
        }
        public Task<Warehouse?> FindWarehouseAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Warehouses.SingleOrDefault(item => item.Id == id));
        public Task<Warehouse?> FindWarehouseByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult(Warehouses.SingleOrDefault(item => item.Code == code));
        public Task<MapLocation?> FindWarehouseLocationAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Locations.SingleOrDefault(item => item.WarehouseId == id));
        public void AddWarehouse(Warehouse item) => Warehouses.Add(item);
        public void AddLocation(MapLocation item) => Locations.Add(item);
        public Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(Guid id, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<TransitTimeRule>>(TransitRules.Where(item => item.WarehouseId == id && (includeInactive || item.IsActive)).ToArray());
        public Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(IReadOnlyCollection<Guid> ids, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<TransitTimeRule>>(TransitRules.Where(item => ids.Contains(item.WarehouseId) && (includeInactive || item.IsActive)).ToArray());
        public void AddTransitRules(IEnumerable<TransitTimeRule> items) => TransitRules.AddRange(items);
        public Task<IReadOnlyCollection<MlorView>> ListMlorAsync(
            Guid? retailerId, Guid? productCategoryId, Guid? productId,
            bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<MlorView>>(Mlors.Where(item =>
                (!retailerId.HasValue || item.RetailerId == retailerId) &&
                (!productCategoryId.HasValue || item.ProductCategoryId == productCategoryId) &&
                (!productId.HasValue || item.ProductId == productId) &&
                (includeInactive || item.IsActive)).Select(item =>
            {
                var retailer = Retailers.Single(value => value.Id == item.RetailerId);
                var category = Categories.SingleOrDefault(value => value.Id == item.ProductCategoryId);
                var product = Products.SingleOrDefault(value => value.Id == item.ProductId);
                return new MlorView(item, retailer.Code, retailer.Name,
                    category?.Code, category?.Name, product?.Sku, product?.Name);
            }).ToArray());
        public Task<RetailerProductMlor?> FindActiveMlorAsync(
            Guid retailerId, Guid? productCategoryId, Guid? productId, CancellationToken cancellationToken) =>
            Task.FromResult(Mlors.SingleOrDefault(item => item.RetailerId == retailerId &&
                item.ProductCategoryId == productCategoryId && item.ProductId == productId && item.IsActive));
        public Task<Retailer?> FindRetailerAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Retailers.SingleOrDefault(item => item.Id == id));
        public Task<Product?> FindProductAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Products.SingleOrDefault(item => item.Id == id));
        public Task<ProductCategory?> FindCategoryAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.SingleOrDefault(item => item.Id == id));
        public void AddMlor(RetailerProductMlor item) => Mlors.Add(item);
        public void AddAuditLog(AuditLog item) => AuditLogs.Add(item);
        public Task<IReadOnlyCollection<SystemConfiguration>> ListCurrentConfigurationsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SystemConfiguration>>(Configurations.Where(item => item.IsActive).ToArray());
        public Task<IReadOnlyCollection<SystemConfiguration>> ListConfigurationHistoryAsync(string key, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SystemConfiguration>>(Configurations.Where(item => item.ConfigKey == key).ToArray());
        public Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Configurations.SingleOrDefault(item => item.ConfigKey == key && item.IsActive));
        public void AddConfiguration(SystemConfiguration item) => Configurations.Add(item);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
