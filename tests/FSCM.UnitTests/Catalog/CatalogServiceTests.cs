using FSCM.Application.Features.Catalog;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Catalog;

public sealed class CatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Published_product_is_locked_for_edits()
    {
        var fixture = new CatalogFixture();
        var category = fixture.AddCategory("CAT-01");
        var product = fixture.AddProduct(category.Id, "SKU001", ProductStatuses.Published);

        var exception = await Assert.ThrowsAsync<CatalogOperationException>(() =>
            fixture.Service.UpdateProductAsync(
                product.Id,
                new UpdateProductData(category.Id, "Changed", "Carton", 100, 10),
                CancellationToken.None));

        Assert.Equal("invalid_transition", exception.Code);
        Assert.Equal("Product SKU001", product.Name);
    }

    [Fact]
    public async Task Publishing_replacement_inactivates_predecessor()
    {
        var fixture = new CatalogFixture();
        var category = fixture.AddCategory("CAT-01");
        var predecessor = fixture.AddProduct(category.Id, "SKU001", ProductStatuses.Published);
        fixture.AddPrice(predecessor.Id, 100_000m);

        var replacement = await fixture.Service.CreateReplacementAsync(
            predecessor.Id,
            new CreateReplacementProductData("SKU002", "Replacement", "Carton", 120, 20, null),
            CancellationToken.None);
        var published = await fixture.Service.PublishProductAsync(replacement.Id, CancellationToken.None);

        Assert.Equal(ProductStatuses.Published, published.Status);
        Assert.Equal(ProductStatuses.Inactive, predecessor.Status);
        Assert.Equal(Now, predecessor.InactivatedAt);
        Assert.Equal(100_000m, published.CurrentUnitPrice);
    }

    [Fact]
    public async Task Publishing_without_price_is_rejected()
    {
        var fixture = new CatalogFixture();
        var category = fixture.AddCategory("CAT-01");
        var product = fixture.AddProduct(category.Id, "SKU001", ProductStatuses.Draft);

        var exception = await Assert.ThrowsAsync<CatalogOperationException>(() =>
            fixture.Service.PublishProductAsync(product.Id, CancellationToken.None));

        Assert.Equal("invalid_transition", exception.Code);
        Assert.Equal(ProductStatuses.Draft, product.Status);
    }

    [Fact]
    public async Task New_price_closes_previous_price_and_preserves_history()
    {
        var fixture = new CatalogFixture();
        var category = fixture.AddCategory("CAT-01");
        var product = fixture.AddProduct(category.Id, "SKU001", ProductStatuses.Published);
        var previous = fixture.AddPrice(product.Id, 100_000m, Now.AddDays(-30));

        var result = await fixture.Service.SetPriceAsync(product.Id, 110_000m, CancellationToken.None);
        var history = await fixture.Service.ListPriceHistoryAsync(product.Id, CancellationToken.None);

        Assert.Equal(110_000m, result.UnitPrice);
        Assert.False(previous.IsActive);
        Assert.Equal(Now, previous.EffectiveTo);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task Category_parent_cycle_is_rejected()
    {
        var fixture = new CatalogFixture();
        var parent = fixture.AddCategory("CAT-01");
        var child = fixture.AddCategory("CAT-02", parent.Id);

        var exception = await Assert.ThrowsAsync<CatalogOperationException>(() =>
            fixture.Service.UpdateCategoryAsync(
                parent.Id,
                child.Id,
                parent.Name,
                parent.Description,
                CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
        Assert.Null(parent.ParentCategoryId);
    }

    [Fact]
    public async Task Category_with_published_product_cannot_be_inactivated()
    {
        var fixture = new CatalogFixture();
        var category = fixture.AddCategory("CAT-01");
        fixture.AddProduct(category.Id, "SKU001", ProductStatuses.Published);

        var exception = await Assert.ThrowsAsync<CatalogOperationException>(() =>
            fixture.Service.SetCategoryStatusAsync(category.Id, false, CancellationToken.None));

        Assert.Equal("invalid_transition", exception.Code);
        Assert.True(category.IsActive);
    }

    [Fact]
    public async Task Draft_product_with_transaction_history_cannot_be_hard_deleted()
    {
        var fixture = new CatalogFixture();
        var product = fixture.AddProduct(fixture.AddCategory("CAT-01").Id, "SKU001", ProductStatuses.Draft);
        fixture.Store.ProductsWithHistory.Add(product.Id);

        var exception = await Assert.ThrowsAsync<CatalogOperationException>(() =>
            fixture.Service.DeleteDraftProductAsync(product.Id, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains(product, fixture.Store.Products);
    }

    [Fact]
    public async Task Create_category_auto_generates_code_when_omitted()
    {
        var fixture = new CatalogFixture();

        var result = await fixture.Service.CreateCategoryAsync(null, null, "Sữa", null, CancellationToken.None);

        Assert.Matches("^CAT-[0-9]{2}$", result.Code);
    }

    [Fact]
    public async Task Create_category_rejects_duplicate_name()
    {
        var fixture = new CatalogFixture();
        await fixture.Service.CreateCategoryAsync(null, null, "Sữa", null, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogOperationException>(() =>
            fixture.Service.CreateCategoryAsync(null, null, "sữa", null, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Delete_category_hard_deletes_when_unused()
    {
        var fixture = new CatalogFixture();
        var created = await fixture.Service.CreateCategoryAsync(null, null, "Sữa", null, CancellationToken.None);

        var result = await fixture.Service.DeleteCategoryAsync(created.Id, CancellationToken.None);

        Assert.True(result.Deleted);
        Assert.False(result.Inactivated);
        Assert.Empty(fixture.Store.Categories);
    }

    [Fact]
    public async Task Delete_category_inactivates_when_in_use()
    {
        var fixture = new CatalogFixture();
        var category = fixture.AddCategory("CAT-20");
        fixture.AddProduct(category.Id, "SKU001", ProductStatuses.Published);

        var result = await fixture.Service.DeleteCategoryAsync(category.Id, CancellationToken.None);

        Assert.False(result.Deleted);
        Assert.True(result.Inactivated);
        Assert.False(category.IsActive);
        Assert.Single(fixture.Store.Categories);
    }

    private sealed class CatalogFixture
    {
        public CatalogFixture()
        {
            Store = new InMemoryCatalogStore();
            Service = new CatalogService(Store, new TestTimeProvider(Now));
        }

        public InMemoryCatalogStore Store { get; }
        public CatalogService Service { get; }

        public ProductCategory AddCategory(string code, Guid? parentId = null)
        {
            var category = new ProductCategory
            {
                Id = Guid.NewGuid(),
                ParentCategoryId = parentId,
                Code = code,
                Name = $"Category {code}",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Store.Categories.Add(category);
            return category;
        }

        public Product AddProduct(Guid categoryId, string sku, string status)
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                ProductCategoryId = categoryId,
                Sku = sku,
                Name = $"Product {sku}",
                Unit = "Carton",
                ShelfLifeHours = 120,
                NearDateThresholdHours = 20,
                Status = status,
                PublishedAt = status == ProductStatuses.Published ? Now.AddDays(-30) : null,
                CreatedAt = Now.AddDays(-30),
                UpdatedAt = Now
            };
            Store.Products.Add(product);
            return product;
        }

        public ProductPrice AddPrice(Guid productId, decimal unitPrice, DateTimeOffset? effectiveFrom = null)
        {
            var price = new ProductPrice
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                UnitPrice = unitPrice,
                EffectiveFrom = effectiveFrom ?? Now,
                IsActive = true,
                CreatedAt = effectiveFrom ?? Now
            };
            Store.Prices.Add(price);
            return price;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryCatalogStore : ICatalogStore
    {
        public List<ProductCategory> Categories { get; } = [];
        public List<Product> Products { get; } = [];
        public List<ProductPrice> Prices { get; } = [];
        public HashSet<Guid> ProductsWithHistory { get; } = [];

        public Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductCategory>>(Categories
                .Where(category => includeInactive || category.IsActive)
                .OrderBy(category => category.Code)
                .ToArray());

        public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.SingleOrDefault(category => category.Id == categoryId));

        public Task<ProductCategory?> FindCategoryByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.SingleOrDefault(category => category.Code == code));

        public Task<ProductCategory?> FindCategoryByNameAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.FirstOrDefault(category =>
                string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> CategoryHasActiveChildrenAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.Any(category =>
                category.ParentCategoryId == categoryId && category.IsActive));

        public Task<bool> CategoryHasActiveProductsAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.Any(product =>
                product.ProductCategoryId == categoryId && product.Status != ProductStatuses.Inactive));

        public Task<bool> CategoryHasAnyChildrenAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.Any(category => category.ParentCategoryId == categoryId));

        public Task<bool> CategoryHasAnyProductsAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.Any(product => product.ProductCategoryId == categoryId));

        private int categoryCodeCounter = 10;
        public Task<string> NextCategoryCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"CAT-{categoryCodeCounter++:00}");

        public void AddCategory(ProductCategory category) => Categories.Add(category);

        public void RemoveCategory(ProductCategory category) => Categories.Remove(category);

        public Task<(IReadOnlyCollection<ProductView> Items, int TotalCount)> ListProductsAsync(
            ProductSearch search,
            CancellationToken cancellationToken)
        {
            var query = Products.AsEnumerable();
            if (search.Search is not null)
            {
                query = query.Where(product =>
                    product.Sku.Contains(search.Search, StringComparison.OrdinalIgnoreCase) ||
                    product.Name.Contains(search.Search, StringComparison.OrdinalIgnoreCase));
            }

            if (search.CategoryId.HasValue)
            {
                query = query.Where(product => product.ProductCategoryId == search.CategoryId.Value);
            }

            if (search.Status is not null)
            {
                query = query.Where(product => product.Status == search.Status);
            }

            var matches = query.ToArray();
            var items = matches
                .Skip((search.Page - 1) * search.PageSize)
                .Take(search.PageSize)
                .Select(CreateView)
                .ToArray();
            return Task.FromResult<(IReadOnlyCollection<ProductView>, int)>((items, matches.Length));
        }

        public async Task<IReadOnlyCollection<ProductView>> ListProductsForQueryAsync(
            ProductQuery query,
            CancellationToken cancellationToken)
        {
            var page = await ListProductsAsync(
                new ProductSearch(query.Search, query.CategoryId, query.Status, 1, 100), cancellationToken);
            return page.Items;
        }

        public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(product => product.Id == productId));

        public Task<Product?> FindProductBySkuAsync(string sku, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(product => product.Sku == sku));

        public Task<Product?> FindReplacementAsync(Guid replacedProductId, CancellationToken cancellationToken) =>
            Task.FromResult(Products.SingleOrDefault(product => product.ReplacesProductId == replacedProductId));

        public Task<ProductView?> FindProductViewAsync(Guid productId, CancellationToken cancellationToken)
        {
            var product = Products.SingleOrDefault(candidate => candidate.Id == productId);
            return Task.FromResult(product is null ? null : CreateView(product));
        }

        public Task<bool> ProductHasTransactionHistoryAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(ProductsWithHistory.Contains(productId));

        public void AddProduct(Product product) => Products.Add(product);
        public void RemoveProduct(Product product) => Products.Remove(product);

        public Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(
            Guid productId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductPrice>>(Prices
                .Where(price => price.ProductId == productId)
                .OrderByDescending(price => price.EffectiveFrom)
                .ToArray());

        public Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(
            IReadOnlyCollection<Guid> productIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductPrice>>(Prices
                .Where(price => productIds.Contains(price.ProductId))
                .OrderBy(price => price.ProductId)
                .ThenByDescending(price => price.EffectiveFrom)
                .ToArray());

        public Task<ProductPrice?> FindActivePriceAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(Prices
                .Where(price => price.ProductId == productId && price.IsActive)
                .OrderByDescending(price => price.EffectiveFrom)
                .FirstOrDefault());

        public void AddPrice(ProductPrice price) => Prices.Add(price);

        public void RemovePrices(IEnumerable<ProductPrice> prices)
        {
            foreach (var price in prices.ToArray())
            {
                Prices.Remove(price);
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ProductView CreateView(Product product)
        {
            var category = Categories.Single(item => item.Id == product.ProductCategoryId);
            var price = Prices
                .Where(item => item.ProductId == product.Id && item.IsActive)
                .OrderByDescending(item => item.EffectiveFrom)
                .FirstOrDefault();
            return new ProductView(product, category.Code, category.Name, price?.UnitPrice);
        }
    }
}
