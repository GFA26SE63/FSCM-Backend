using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Catalog.Domain.Categories;
using FSCM.Modules.Catalog.Domain.Products;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Catalog.Application;

public sealed record CategoryInput(string Name, bool Active = true);

public sealed class CategoryService(ICategoryRepository repo, ICatalogUnitOfWork uow, IdGenerator ids)
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct) => (await repo.AllAsync(ct)).Select(Map).ToList();

    public async Task<string> CreateAsync(CategoryInput x, CancellationToken ct)
    {
        var code = await ids.NextAsync("CAT", n => IdFormat.Category((int)n), ct);
        repo.Add(new Category(code, x.Name)); await uow.SaveChangesAsync(ct); return code;
    }

    public async Task UpdateAsync(string code, CategoryInput x, CancellationToken ct)
    {
        var c = await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("category.not_found", $"Không có danh mục {code}.");
        c.Rename(x.Name); c.SetActive(x.Active); await uow.SaveChangesAsync(ct);
    }

    internal static CategoryDto Map(Category c) => new(c.Id, c.Name, c.Active);
}

public sealed class ProductService(IProductRepository products, ICategoryRepository categories, ICatalogUnitOfWork uow, IdGenerator ids, IClock clock)
{
    public async Task<PagedResult<ProductDto>> SearchAsync(PageRequest page, string? status, string? category, CancellationToken ct)
    {
        ProductStatus? st = Enum.TryParse<ProductStatus>(status, true, out var s) ? s : null;
        return (await products.SearchAsync(page.Search, st, category, ct)).Select(Map).Page(page);
    }

    public async Task<ProductDto> GetAsync(string sku, CancellationToken ct) => Map(await Load(sku, ct));

    public async Task<string> CreateDraftAsync(ProductDetails d, CancellationToken ct)
    {
        await EnsureCategory(d.CategoryCode, ct);
        var sku = await ids.NextAsync("SKU", n => IdFormat.Sku((int)n), ct);
        products.Add(new Product(sku, d, clock.UtcNow)); await uow.SaveChangesAsync(ct); return sku;
    }

    public async Task UpdateDraftAsync(string sku, ProductDetails d, CancellationToken ct)
    {
        await EnsureCategory(d.CategoryCode, ct);
        (await Load(sku, ct)).UpdateDraft(d); await uow.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(string sku, CancellationToken ct)
    {
        var p = await Load(sku, ct); p.Publish(clock.UtcNow);
        uow.Enqueue(new ProductPublished(p.Id, p.Name, p.CategoryCode, p.BasePrice));
        await uow.SaveChangesAsync(ct);
    }

    public async Task InactivateAsync(string sku, CancellationToken ct)
    {
        var p = await Load(sku, ct); p.Inactivate();
        uow.Enqueue(new ProductInactivated(p.Id));
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Clone a (published) product to a new SKU in Draft, so it can be edited and published separately.</summary>
    public async Task<string> CloneAsync(string sku, CancellationToken ct)
    {
        var p = await Load(sku, ct);
        var newSku = await ids.NextAsync("SKU", n => IdFormat.Sku((int)n), ct);
        products.Add(p.CloneAs(newSku, clock.UtcNow)); await uow.SaveChangesAsync(ct); return newSku;
    }

    private async Task<Product> Load(string sku, CancellationToken ct) =>
        await products.GetAsync(sku, ct) ?? throw new BusinessRuleException("product.not_found", $"Không có sản phẩm {sku}.");
    private async Task EnsureCategory(string code, CancellationToken ct) =>
        Guard.Against(await categories.GetAsync(code, ct) is null, "category.not_found", $"Không có danh mục {code}.");

    internal static ProductDto Map(Product p) => new(p.Id, p.Name, p.CategoryCode, p.Pack, p.UnitsPerCase, p.ShelfLifeDays, p.NearExpiryDays,
        p.BasePrice, p.CostPrice, p.DefaultMlorRatio, p.Status.ToString(), p.ImageUrl, p.ClonedFromSku);
}

public sealed class CatalogModule(IProductRepository products, ICategoryRepository categories) : ICatalogModule
{
    public async Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default) =>
        await products.GetAsync(sku, ct) is { } p ? ProductService.Map(p) : null;
    public async Task<IReadOnlyList<ProductDto>> PublishedProductsAsync(CancellationToken ct = default) =>
        (await products.SearchAsync(null, ProductStatus.Published, null, ct)).Select(ProductService.Map).ToList();
    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct = default) =>
        (await categories.AllAsync(ct)).Select(CategoryService.Map).ToList();
}

public static class CatalogApplication
{
    public static IServiceCollection AddCatalogApplication(this IServiceCollection s) => s
        .AddScoped<ICatalogModule, CatalogModule>()
        .AddScoped<CategoryService>()
        .AddScoped<ProductService>();
}
