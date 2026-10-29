using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Catalog;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class ProductCloneApiTests : IClassFixture<IdentityAuthorizationTests.IdentityApiFactory>
{
    private readonly IdentityAuthorizationTests.IdentityApiFactory identityFactory;

    public ProductCloneApiTests(IdentityAuthorizationTests.IdentityApiFactory identityFactory) =>
        this.identityFactory = identityFactory;

    [Fact]
    public async Task Clone_route_requires_admin_and_uses_product_guid()
    {
        var store = new CloneCatalogStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var request = new { sku = "SKU002" };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"/api/v1/products/{store.Source.Id}/clone", request)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.CreateToken(identityFactory.Sales));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/v1/products/{store.Source.Id}/clone", request)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.CreateToken(identityFactory.Admin));
        var response = await client.PostAsJsonAsync($"/api/v1/products/{store.Source.Id}/clone", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(body).RootElement;
        var cloneId = json.GetProperty("id").GetGuid();
        Assert.Equal("SKU002", json.GetProperty("sku").GetString());
        Assert.Equal(ProductStatuses.Draft, json.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("replacesProductId").ValueKind);
        Assert.Equal($"/api/v1/products/{cloneId}", response.Headers.Location?.AbsolutePath);

        Assert.Equal(ProductStatuses.Published, store.Source.Status);
        Assert.Null(store.Source.InactivatedAt);
        Assert.DoesNotContain(cloneId, store.BatchProductIds);
        Assert.DoesNotContain(cloneId, store.InventoryProductIds);
        Assert.DoesNotContain(cloneId, store.ReservationProductIds);
        Assert.DoesNotContain(cloneId, store.TransactionProductIds);
    }

    [Fact]
    public async Task Clone_route_rejects_duplicate_sku_without_side_effects()
    {
        var store = new CloneCatalogStore();
        store.Products.Add(new Product
        {
            Id = Guid.NewGuid(),
            ProductCategoryId = store.Category.Id,
            Sku = "SKU002",
            Name = "Sản phẩm đã tồn tại",
            Unit = "Thùng",
            ShelfLifeHours = 120,
            NearDateThresholdHours = 20,
            Status = ProductStatuses.Draft
        });
        var initialProductCount = store.Products.Count;
        var initialPriceCount = store.Prices.Count;
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.CreateToken(identityFactory.Admin));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/products/{store.Source.Id}/clone",
            new { sku = "sku002" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(initialProductCount, store.Products.Count);
        Assert.Equal(initialPriceCount, store.Prices.Count);
        Assert.Equal(ProductStatuses.Published, store.Source.Status);
    }

    private WebApplicationFactory<Program> CreateFactory(CloneCatalogStore store) =>
        identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICatalogStore>();
            services.AddSingleton<ICatalogStore>(store);
        }));

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private sealed class CloneCatalogStore : ICatalogStore
    {
        public CloneCatalogStore()
        {
            Category = new ProductCategory
            {
                Id = Guid.NewGuid(),
                Code = "CAT-01",
                Name = "Sữa và sản phẩm từ sữa",
                IsActive = true
            };
            Source = new Product
            {
                Id = Guid.NewGuid(),
                ProductCategoryId = Category.Id,
                Sku = "SKU001",
                Name = "Sữa tươi tiệt trùng",
                Unit = "Thùng",
                ShelfLifeHours = 240,
                NearDateThresholdHours = 24,
                Status = ProductStatuses.Published,
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-30),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Products.Add(Source);
            Prices.Add(new ProductPrice
            {
                Id = Guid.NewGuid(),
                ProductId = Source.Id,
                UnitPrice = 100_000m,
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });
            BatchProductIds.Add(Source.Id);
            InventoryProductIds.Add(Source.Id);
            ReservationProductIds.Add(Source.Id);
            TransactionProductIds.Add(Source.Id);
        }

        public ProductCategory Category { get; }
        public Product Source { get; }
        public List<Product> Products { get; } = [];
        public List<ProductPrice> Prices { get; } = [];
        public HashSet<Guid> BatchProductIds { get; } = [];
        public HashSet<Guid> InventoryProductIds { get; } = [];
        public HashSet<Guid> ReservationProductIds { get; } = [];
        public HashSet<Guid> TransactionProductIds { get; } = [];

        public Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductCategory>>([Category]);
        public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult<ProductCategory?>(categoryId == Category.Id ? Category : null);
        public Task<ProductCategory?> FindCategoryByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult<ProductCategory?>(null);
        public Task<ProductCategory?> FindCategoryByNameAsync(string name, CancellationToken cancellationToken) => Task.FromResult<ProductCategory?>(null);
        public Task<bool> CategoryHasActiveChildrenAsync(Guid categoryId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> CategoryHasActiveProductsAsync(Guid categoryId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> CategoryHasAnyChildrenAsync(Guid categoryId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> CategoryHasAnyProductsAsync(Guid categoryId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<string> NextCategoryCodeAsync(CancellationToken cancellationToken) => Task.FromResult("CAT-02");
        public void AddCategory(ProductCategory category) { }
        public void RemoveCategory(ProductCategory category) { }
        public Task<(IReadOnlyCollection<ProductView> Items, int TotalCount)> ListProductsAsync(ProductSearch search, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyCollection<ProductView>, int)>((Products.Select(View).ToArray(), Products.Count));
        public Task<IReadOnlyCollection<ProductView>> ListProductsForQueryAsync(ProductQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductView>>(Products.Select(View).ToArray());
        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(product => product.Id == productId));
        public Task<Product?> FindProductBySkuAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(product => product.Sku == sku));
        public Task<Product?> FindReplacementAsync(Guid replacedProductId, CancellationToken cancellationToken) => Task.FromResult<Product?>(null);
        public Task<ProductView?> FindProductViewAsync(Guid productId, CancellationToken cancellationToken)
        {
            var product = Products.SingleOrDefault(item => item.Id == productId);
            return Task.FromResult(product is null ? null : View(product));
        }
        public Task<bool> ProductHasTransactionHistoryAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(TransactionProductIds.Contains(productId));
        public void AddProduct(Product product) => Products.Add(product);
        public void RemoveProduct(Product product) => Products.Remove(product);
        public Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductPrice>>(Prices.Where(price => price.ProductId == productId).ToArray());
        public Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductPrice>>(Prices.Where(price => productIds.Contains(price.ProductId)).ToArray());
        public Task<ProductPrice?> FindActivePriceAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Prices.SingleOrDefault(price => price.ProductId == productId && price.IsActive));
        public void AddPrice(ProductPrice price) => Prices.Add(price);
        public void RemovePrices(IEnumerable<ProductPrice> prices) => Prices.RemoveAll(prices.Contains);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ProductView View(Product product) => new(
            product,
            Category.Code,
            Category.Name,
            Prices.SingleOrDefault(price => price.ProductId == product.Id && price.IsActive)?.UnitPrice);
    }
}
