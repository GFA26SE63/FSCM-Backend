namespace FSCM.Application.Features.NearExpiryOffers;

/// <summary>
/// One cart line as Sales submits it, online or in an offline sync batch. The client states what
/// the customer accepted; the server never fills in a missing acceptance or lowers a ratio itself.
/// </summary>
public sealed record OrderLineAcceptanceRequest(
    Guid ProductId,
    string LineType,
    decimal OrderedQuantity,
    Guid? NearExpiryOfferId,
    int? NearExpiryOfferVersion,
    decimal? AcceptedNetUnitPrice,
    decimal? AcceptedMlorRatio,
    bool CustomerAccepted,
    Guid? AcceptedByUserId,
    DateTimeOffset? AcceptedAt);

/// <summary>
/// A validated line. The order module persists these fields verbatim onto SALES_ORDER_LINE, and
/// the allocation run reads them without re-deriving the ratio.
/// </summary>
public sealed record AcceptedOrderLine(
    Guid ProductId,
    string Sku,
    string ProductName,
    string LineType,
    decimal OrderedQuantity,
    Guid? NearExpiryOfferId,
    int? NearExpiryOfferVersion,
    string? NearExpiryOfferCode,
    decimal? AcceptedNetUnitPrice,
    decimal EffectiveMlorRatio,
    string MlorSource,
    decimal? DefaultMlorRatio,
    Guid? AcceptedByUserId,
    DateTimeOffset? AcceptedAt,
    int TotalShelfLifeHours,
    IReadOnlyCollection<Guid> EligibleBatchIds);

/// <summary>
/// One published offer version. The batch-discount module owns these rows; this module only reads
/// them, and a line stays bound to the version it was accepted against.
/// </summary>
public sealed record NearExpiryOfferView(
    Guid OfferId,
    int Version,
    string OfferCode,
    Guid ProductId,
    string Sku,
    decimal MlorRatio,
    decimal NetUnitPrice,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool IsConfirmed,
    bool IsActive,
    IReadOnlyCollection<Guid> EligibleBatchIds);

/// <summary>Effective MLOR for one line plus where it came from, for traces and the portal.</summary>
public sealed record EffectiveMlorResult(
    decimal Ratio,
    string Source,
    decimal? DefaultRatio,
    int TotalShelfLifeHours);
