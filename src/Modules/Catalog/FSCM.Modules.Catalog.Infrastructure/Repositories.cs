using FSCM.Modules.Catalog.Application;
using FSCM.Modules.Catalog.Domain.Categories;
using FSCM.Modules.Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Catalog.Infrastructure;

internal sealed class ProductRepository(CatalogDbContext db) : IProductRepository
{
    public Task<Product?> GetAsync(string sku, CancellationToken ct) => db.Products.FirstOrDefaultAsync(p => p.Id == sku, ct);
    public async Task<IReadOnlyList<Product>> SearchAsync(string? search, ProductStatus? status, string? categoryCode, CancellationToken ct)
    {
        var q = db.Products.AsQueryable();
        if (status is not null) q = q.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(categoryCode)) q = q.Where(p => p.CategoryCode == categoryCode);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(p => p.Id.Contains(search) || p.Name.ToLower().Contains(search.ToLower()));
        return await q.OrderBy(p => p.Id).ToListAsync(ct);
    }
    public void Add(Product p) => db.Products.Add(p);
}
internal sealed class CategoryRepository(CatalogDbContext db) : ICategoryRepository
{
    public Task<Category?> GetAsync(string code, CancellationToken ct) => db.Categories.FirstOrDefaultAsync(c => c.Id == code, ct);
    public async Task<IReadOnlyList<Category>> AllAsync(CancellationToken ct) => await db.Categories.OrderBy(c => c.Id).ToListAsync(ct);
    public void Add(Category c) => db.Categories.Add(c);
}
