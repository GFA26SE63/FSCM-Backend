using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Catalog;

public sealed record ProductView(
    Product Product,
    string CategoryCode,
    string CategoryName,
    decimal? CurrentUnitPrice);

public interface ICatalogStore
{
    Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<ProductCategory?> FindCategoryByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> CategoryHasActiveChildrenAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<bool> CategoryHasActiveProductsAsync(Guid categoryId, CancellationToken cancellationToken);
    void AddCategory(ProductCategory category);

    Task<(IReadOnlyCollection<ProductView> Items, int TotalCount)> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProductView>> ListProductsForQueryAsync(
        ProductQuery query,
        CancellationToken cancellationToken);

    Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<Product?> FindProductBySkuAsync(string sku, CancellationToken cancellationToken);
    Task<Product?> FindReplacementAsync(Guid replacedProductId, CancellationToken cancellationToken);
    Task<ProductView?> FindProductViewAsync(Guid productId, CancellationToken cancellationToken);
    Task<bool> ProductHasTransactionHistoryAsync(Guid productId, CancellationToken cancellationToken);
    void AddProduct(Product product);
    void RemoveProduct(Product product);

    Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(
        Guid productId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProductPrice>> ListPricesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    Task<ProductPrice?> FindActivePriceAsync(Guid productId, CancellationToken cancellationToken);
    void AddPrice(ProductPrice price);
    void RemovePrices(IEnumerable<ProductPrice> prices);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
