namespace FSCM.API.Contracts.ShelfLifeDiscounts;

public sealed record ShelfLifeDiscountTierRequest(
    string Scope,
    Guid? ProductId,
    Guid? ProductCategoryId,
    decimal MinRemainingPercent,
    decimal MaxRemainingPercent,
    string DiscountType,
    decimal DiscountValue);

public sealed record PublishShelfLifeDiscountTableRequest(
    DateTimeOffset EffectiveFrom,
    string? Note,
    IReadOnlyCollection<ShelfLifeDiscountTierRequest> Tiers);
