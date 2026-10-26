namespace FSCM.Application.Features.ShelfLifeDiscounts;

public static class ShelfLifeDiscountScopes
{
    /// <summary>A rule for one SKU. Beats a Category rule covering the same remaining life.</summary>
    public const string Sku = "Sku";

    public const string Category = "Category";
}

public static class ShelfLifeDiscountTypes
{
    /// <summary>A percentage off the base price, capped at 100.</summary>
    public const string Percent = "Percent";

    /// <summary>A fixed amount off each unit; the final price is floored at zero.</summary>
    public const string FixedPerUnit = "FixedPerUnit";
}

/// <summary>
/// One band of remaining shelf life. <paramref name="MinRemainingPercent"/> is inclusive and
/// <paramref name="MaxRemainingPercent"/> is exclusive, so adjacent bands meet without overlapping
/// and a given percentage resolves to exactly one band.
/// </summary>
public sealed record ShelfLifeDiscountTierData(
    string Scope,
    Guid? ProductId,
    Guid? ProductCategoryId,
    decimal MinRemainingPercent,
    decimal MaxRemainingPercent,
    string DiscountType,
    decimal DiscountValue);

public sealed record PublishShelfLifeDiscountTableData(
    DateTimeOffset EffectiveFrom,
    string? Note,
    IReadOnlyCollection<ShelfLifeDiscountTierData> Tiers);

public sealed record ShelfLifeDiscountTierResult(
    Guid Id,
    string Scope,
    Guid? ProductId,
    Guid? ProductCategoryId,
    decimal MinRemainingPercent,
    decimal MaxRemainingPercent,
    string DiscountType,
    decimal DiscountValue);

public sealed record ShelfLifeDiscountTableResult(
    Guid Id,
    int Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    string? Note,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ShelfLifeDiscountTierResult> Tiers);

/// <summary>
/// The single tier that applies to one allocated part, with the table version it came from.
/// <paramref name="Tier"/> is null when nothing matches, which means a reduction of zero rather
/// than an error: most stock is not near expiry.
/// </summary>
/// <summary>
/// What the shelf-life reduction came to for one part, and where it came from.
///
/// <see cref="TableId"/> is null and <see cref="Version"/> is 0 when no table was in force at all.
/// That is a real answer, not a failure: the reduction is zero and the part keeps its base price,
/// exactly as it does when a table is in force but no band covers the remaining percentage.
/// </summary>
public sealed record ShelfLifeDiscountMatch(
    Guid? TableId,
    int Version,
    decimal RemainingPercent,
    ShelfLifeDiscountTierResult? Tier,
    decimal DiscountAmountPerUnit,
    string Explanation);
