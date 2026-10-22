namespace FSCM.API.Contracts.TransitTables;

public sealed record TransitBandRequest(
    int MinDistanceMeters,
    int MaxDistanceMeters,
    int HandlingMinutes,
    int TransitMinutes,
    decimal EstimatedCostAmount);

public sealed record PublishTransitTableRequest(
    int MaxServingDistanceMeters,
    DateTimeOffset EffectiveFrom,
    string? Note,
    IReadOnlyCollection<TransitBandRequest> Bands);
