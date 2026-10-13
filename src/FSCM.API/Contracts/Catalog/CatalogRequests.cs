namespace FSCM.API.Contracts.Catalog;

public sealed record CreateCategoryRequest(
    Guid? ParentCategoryId,
    string Code,
    string Name,
    string? Description);

public sealed record UpdateCategoryRequest(
    Guid? ParentCategoryId,
    string Name,
    string? Description);

public sealed record UpdateCategoryStatusRequest(bool IsActive);

public sealed record CreateProductRequest(
    Guid ProductCategoryId,
    string Sku,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours,
    decimal? InitialUnitPrice);

public sealed record UpdateProductRequest(
    Guid ProductCategoryId,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours);

public sealed record CreateReplacementProductRequest(
    string Sku,
    string Name,
    string Unit,
    int ShelfLifeHours,
    int NearDateThresholdHours,
    decimal? InitialUnitPrice);

public sealed record SetProductPriceRequest(decimal UnitPrice);
