namespace FSCM.Application.Features.Promotions;

public sealed record CreatePromotionData(
    string Name, decimal MinimumQuantity, decimal DiscountPercent,
    DateTimeOffset StartAt, DateTimeOffset EndAt,
    IReadOnlyCollection<Guid> ProductIds, IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> RetailerIds);

public sealed record UpdatePromotionData(
    string Name, decimal MinimumQuantity, decimal DiscountPercent,
    DateTimeOffset StartAt, DateTimeOffset EndAt,
    IReadOnlyCollection<Guid> ProductIds, IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> RetailerIds);

public sealed record ApplyBatchDiscountData(
    decimal DiscountPercent, DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record PromotionResult(
    Guid Id, string Code, string Name, string PromotionType, decimal MinimumQuantity,
    decimal DiscountPercent, DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsActive,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyCollection<Guid> ProductIds, IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> RetailerIds);

public sealed record BatchDiscountResult(
    Guid Id, Guid BatchId, string BatchCode, Guid WarehouseId, string WarehouseCode,
    Guid ProductId, string Sku, string ProductName, DateOnly ExpirationDate,
    Guid PromotionId, string PromotionCode, decimal DiscountPercent,
    DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsActive,
    Guid AssignedByUserId, DateTimeOffset AssignedAt, DateTimeOffset? RemovedAt);

public sealed record BatchDiscountSuggestionResult(
    Guid BatchId, string BatchCode, Guid WarehouseId, string WarehouseCode,
    Guid ProductId, string Sku, string ProductName, DateOnly ExpirationDate,
    decimal AvailableQuantity, int RemainingShelfLifeDays,
    decimal RemainingShelfLifePercent, decimal SuggestedDiscountPercent,
    decimal? CurrentDiscountPercent);

public sealed record AppliedPromotionSnapshot(
    Guid PromotionId, string Code, string Name, string PromotionType,
    decimal DiscountPercent, decimal MinimumQuantity,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo);

public sealed record PriceSnapshotResult(
    Guid ProductId, string Sku, string ProductName, Guid? BatchId, string? BatchCode,
    Guid? RetailerId, decimal Quantity, decimal UnitPrice, decimal GrossAmount,
    decimal DiscountPercent, decimal DiscountAmount, decimal NetUnitPrice,
    decimal NetAmount, AppliedPromotionSnapshot? AppliedPromotion,
    IReadOnlyCollection<AppliedPromotionSnapshot> EligiblePromotions,
    DateTimeOffset PricedAt,
    bool FinalPricePendingAllocation,
    Guid ProductPriceId,
    DateTimeOffset ProductPriceEffectiveFrom,
    DateTimeOffset? ProductPriceEffectiveTo)
{
    public decimal BaseUnitPrice => UnitPrice;
    public decimal OrdinaryPromotionDiscountAmount => DiscountAmount;
    public decimal IndicativeNetUnitPrice => NetUnitPrice;
    public decimal IndicativeNetAmount => NetAmount;
}
