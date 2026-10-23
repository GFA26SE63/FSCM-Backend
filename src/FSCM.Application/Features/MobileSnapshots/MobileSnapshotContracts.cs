namespace FSCM.Application.Features.MobileSnapshots;

public sealed record MobileSnapshotRetailer(
    Guid Id, string Code, string Name, string? Address, string Status, int CreditTermDays,
    DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotProduct(
    Guid Id, Guid CategoryId, string Sku, string Name, string Unit, int ShelfLifeHours,
    string Status, DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotCategory(
    Guid Id, Guid? ParentCategoryId, string Code, string Name, bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotPrice(
    Guid Id, Guid ProductId, decimal UnitPrice, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, bool IsActive, DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotPromotion(
    Guid Id, string Code, string Name, string Type, decimal MinimumQuantity,
    decimal DiscountPercent, DateTimeOffset StartAt, DateTimeOffset EndAt, bool IsActive,
    IReadOnlyCollection<Guid> ProductIds, IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> RetailerIds, DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotMlor(
    Guid Id, Guid RetailerId, Guid? CategoryId, Guid? ProductId, decimal MinimumLifeRatio,
    int? MinimumLifeHours, int Version, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, bool IsActive, DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotStock(
    Guid ProductId, decimal IndicativeAvailableQuantity, DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotDefaultMlor(
    decimal MinimumLifeRatio, int Version, DateTimeOffset EffectiveFrom,
    DateTimeOffset UpdatedAt);

public sealed record MobileSnapshotData(
    IReadOnlyCollection<MobileSnapshotRetailer> Retailers,
    IReadOnlyCollection<MobileSnapshotCategory> Categories,
    IReadOnlyCollection<MobileSnapshotProduct> Products,
    IReadOnlyCollection<MobileSnapshotPrice> Prices,
    IReadOnlyCollection<MobileSnapshotPromotion> Promotions,
    IReadOnlyCollection<MobileSnapshotMlor> Mlors,
    IReadOnlyCollection<MobileSnapshotStock> IndicativeStock,
    MobileSnapshotDefaultMlor? DefaultMlor,
    DateTimeOffset LatestChangedAt);

public sealed record MobileSnapshotResult(
    string Version,
    string Checksum,
    string DeltaMode,
    bool NotModified,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? ChangesSince,
    IReadOnlyCollection<MobileSnapshotRetailer> Retailers,
    IReadOnlyCollection<MobileSnapshotCategory> Categories,
    IReadOnlyCollection<MobileSnapshotProduct> Products,
    IReadOnlyCollection<MobileSnapshotPrice> Prices,
    IReadOnlyCollection<MobileSnapshotPromotion> Promotions,
    IReadOnlyCollection<MobileSnapshotMlor> Mlors,
    IReadOnlyCollection<MobileSnapshotStock> IndicativeStock,
    MobileSnapshotDefaultMlor? DefaultMlor,
    string StockDisclaimer,
    string PriceDisclaimer);

public interface IMobileSnapshotStore
{
    Task<MobileSnapshotData> LoadAsync(Guid salesUserId, DateTimeOffset? changedAfter,
        CancellationToken cancellationToken);
}
