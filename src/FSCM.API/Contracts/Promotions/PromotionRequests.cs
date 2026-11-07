using FSCM.Application.Features.Promotions;

namespace FSCM.API.Contracts.Promotions;

public sealed record CreatePromotionRequest(
    string Name, decimal MinimumQuantity, decimal DiscountPercent,
    DateTimeOffset StartAt, DateTimeOffset EndAt,
    IReadOnlyCollection<Guid> ProductIds, IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> RetailerIds,
    string CampaignMode = "ProductGroup", IReadOnlyCollection<PromotionProductRule>? ProductRules = null, bool ActivateOnSave = false);

public sealed record UpdatePromotionRequest(
    string Name, decimal MinimumQuantity, decimal DiscountPercent,
    DateTimeOffset StartAt, DateTimeOffset EndAt,
    IReadOnlyCollection<Guid> ProductIds, IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<Guid> RetailerIds,
    string CampaignMode = "ProductGroup", IReadOnlyCollection<PromotionProductRule>? ProductRules = null);

public sealed record ApplyBatchDiscountRequest(
    decimal DiscountPercent, DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record CartPriceSnapshotRequest(
    Guid RetailerId,
    IReadOnlyCollection<CartPriceSnapshotLineRequest> Lines,
    DateTimeOffset? At = null);

public sealed record CartPriceSnapshotLineRequest(Guid ProductId, decimal Quantity);
