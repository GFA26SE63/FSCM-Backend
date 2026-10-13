using FSCM.BuildingBlocks.Application;

namespace FSCM.Modules.Catalog.Contracts;

public sealed record ProductDto(
    string Sku, string Name, string CategoryCode, string Pack, int UnitsPerCase,
    int ShelfLifeDays, int NearExpiryDays, decimal BasePrice, decimal CostPrice, decimal DefaultMlorRatio,
    string Status, string? ImageUrl, string? ClonedFromSku);

public sealed record CategoryDto(string Code, string Name, bool Active);

/// <summary>Read-only product data for Ordering, Pricing, Inventory (shelf life, MLOR ratio, prices).</summary>
public interface ICatalogModule
{
    Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> PublishedProductsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct = default);
}

public sealed record ProductPublished(string Sku, string Name, string CategoryCode, decimal BasePrice) : IntegrationEvent;
public sealed record ProductInactivated(string Sku) : IntegrationEvent;
