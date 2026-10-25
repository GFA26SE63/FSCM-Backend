namespace FSCM.Application.Features.Catalog;

public sealed record CatalogPage<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record CategoryResult(
    Guid Id,
    Guid? ParentCategoryId,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Outcome of deleting a category: either a hard delete or an inactivation (soft delete).</summary>
public sealed record DeleteCategoryResult(Guid CategoryId, bool Deleted, bool Inactivated);

public sealed record ProductResult(
    Guid Id,
    Guid ProductCategoryId,
    string CategoryCode,
    string CategoryName,
    Guid? ReplacesProductId,
    string Sku,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours,
    string Status,
    decimal? CurrentUnitPrice,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? InactivatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductPriceResult(
    Guid Id,
    Guid ProductId,
    decimal UnitPrice,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record ProductPriceSuggestion(
    Guid ProductId,
    Guid SourceProductId,
    decimal SuggestedUnitPrice,
    decimal AverageRecentUnitPrice,
    decimal MinimumRecentUnitPrice,
    decimal MaximumRecentUnitPrice,
    int SampleSize,
    DateTimeOffset LatestEffectiveFrom);

public sealed record ProductSearch(
    string? Search,
    Guid? CategoryId,
    string? Status,
    int Page,
    int PageSize);

public sealed record ProductQuery(
    string? Search,
    Guid? CategoryId,
    string? Status);

public sealed record CreateProductData(
    Guid ProductCategoryId,
    string Sku,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours,
    decimal? InitialUnitPrice);

public sealed record UpdateProductData(
    Guid ProductCategoryId,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours);

public sealed record CreateReplacementProductData(
    string Sku,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours,
    decimal? InitialUnitPrice);

public sealed record CloneProductData(
    string Sku,
    Guid? ProductCategoryId,
    string? Name,
    string? Unit,
    int? ShelfLifeHours,
    int? NearDateThresholdHours,
    decimal? InitialUnitPrice);
