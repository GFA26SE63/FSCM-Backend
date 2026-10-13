using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Domain.Categories;
using FSCM.Modules.Catalog.Domain.Products;

namespace FSCM.Modules.Catalog.Application;

public interface ICatalogUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IProductRepository
{
    Task<Product?> GetAsync(string sku, CancellationToken ct);
    Task<IReadOnlyList<Product>> SearchAsync(string? search, ProductStatus? status, string? categoryCode, CancellationToken ct);
    void Add(Product p);
}

public interface ICategoryRepository
{
    Task<Category?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Category>> AllAsync(CancellationToken ct);
    void Add(Category c);
}
