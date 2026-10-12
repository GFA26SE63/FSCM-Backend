using FSCM.Application.Features.Catalog;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Catalog;

public sealed class CatalogStore(AppDbContext dbContext) : ICatalogStore
{
    public async Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProductCategoryRecords.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query.OrderBy(category => category.Code).ToArrayAsync(cancellationToken);
    }

    public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.ProductCategoryRecords.SingleOrDefaultAsync(
            category => category.Id == categoryId,
            cancellationToken);

    public Task<ProductCategory?> FindCategoryByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.ProductCategoryRecords.SingleOrDefaultAsync(
            category => category.Code == code,
            cancellationToken);

    public Task<bool> CategoryHasActiveChildrenAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.ProductCategoryRecords.AnyAsync(
            category => category.ParentCategoryId == categoryId && category.IsActive,
            cancellationToken);

    public Task<bool> CategoryHasActiveProductsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.AnyAsync(
            product => product.ProductCategoryId == categoryId && product.Status != ProductStatuses.Inactive,
            cancellationToken);

    public void AddCategory(ProductCategory category) => dbContext.ProductCategoryRecords.Add(category);

    public async Task<(IReadOnlyCollection<ProductView> Items, int TotalCount)> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProductRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.ToLower();
            query = query.Where(product =>
                product.Sku.ToLower().Contains(term) || product.Name.ToLower().Contains(term));
        }

        if (search.CategoryId.HasValue)
        {
            query = query.Where(product => product.ProductCategoryId == search.CategoryId.Value);
        }

        if (search.Status is not null)
        {
            query = query.Where(product => product.Status == search.Status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var records = await CreateProductViewQuery(query)
            .OrderBy(record => record.Product.Sku)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToArrayAsync(cancellationToken);

        return (records.Select(ToProductView).ToArray(), totalCount);
    }

    public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

    public Task<Product?> FindProductBySkuAsync(string sku, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.SingleOrDefaultAsync(product => product.Sku == sku, cancellationToken);

    public Task<Product?> FindReplacementAsync(Guid replacedProductId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.SingleOrDefaultAsync(
            product => product.ReplacesProductId == replacedProductId,
            cancellationToken);

    public async Task<ProductView?> FindProductViewAsync(Guid productId, CancellationToken cancellationToken)
    {
        var record = await CreateProductViewQuery(
                dbContext.ProductRecords.AsNoTracking().Where(product => product.Id == productId))
            .SingleOrDefaultAsync(cancellationToken);
        return record is null ? null : ToProductView(record);
    }

    public void AddProduct(Product product) => dbContext.ProductRecords.Add(product);

    public void RemoveProduct(Product product) => dbContext.ProductRecords.Remove(product);

    public async Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(
        Guid productId,
        CancellationToken cancellationToken) =>
        await dbContext.ProductPriceRecords
            .Where(price => price.ProductId == productId)
            .OrderByDescending(price => price.EffectiveFrom)
            .ToArrayAsync(cancellationToken);

    public Task<ProductPrice?> FindActivePriceAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.ProductPriceRecords
            .Where(price => price.ProductId == productId && price.IsActive)
            .OrderByDescending(price => price.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public void AddPrice(ProductPrice price) => dbContext.ProductPriceRecords.Add(price);

    public void RemovePrices(IEnumerable<ProductPrice> prices) => dbContext.ProductPriceRecords.RemoveRange(prices);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<ProductViewRecord> CreateProductViewQuery(IQueryable<Product> products) =>
        from product in products
        join category in dbContext.ProductCategoryRecords.AsNoTracking()
            on product.ProductCategoryId equals category.Id
        select new ProductViewRecord(
            product,
            category.Code,
            category.Name,
            dbContext.ProductPriceRecords
                .Where(price => price.ProductId == product.Id && price.IsActive)
                .OrderByDescending(price => price.EffectiveFrom)
                .Select(price => (decimal?)price.UnitPrice)
                .FirstOrDefault());

    private static ProductView ToProductView(ProductViewRecord record) => new(
        record.Product,
        record.CategoryCode,
        record.CategoryName,
        record.CurrentUnitPrice);

    private sealed record ProductViewRecord(
        Product Product,
        string CategoryCode,
        string CategoryName,
        decimal? CurrentUnitPrice);
}
