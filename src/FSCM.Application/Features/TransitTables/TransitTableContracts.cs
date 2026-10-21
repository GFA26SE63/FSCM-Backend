namespace FSCM.Application.Features.TransitTables;

/// <summary>
/// One distance band. <paramref name="MinDistanceMeters"/> is inclusive and
/// <paramref name="MaxDistanceMeters"/> is exclusive.
/// </summary>
public sealed record TransitBandData(
    int MinDistanceMeters,
    int MaxDistanceMeters,
    int HandlingMinutes,
    int TransitMinutes,
    decimal EstimatedCostAmount);

public sealed record PublishTransitTableData(
    int MaxServingDistanceMeters,
    DateTimeOffset EffectiveFrom,
    string? Note,
    IReadOnlyCollection<TransitBandData> Bands);

public sealed record TransitBandResult(
    Guid Id,
    int MinDistanceMeters,
    int MaxDistanceMeters,
    int HandlingMinutes,
    int TransitMinutes,
    decimal EstimatedCostAmount);

public sealed record TransitTableResult(
    Guid Id,
    int Version,
    int MaxServingDistanceMeters,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    string? Note,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<TransitBandResult> Bands);

/// <summary>
/// The band that serves one distance, with the table version it came from so a run that used an
/// older version can still be explained.
/// </summary>
public sealed record TransitLookupResult(
    Guid TransitTableId,
    int Version,
    int DistanceMeters,
    int MinDistanceMeters,
    int MaxDistanceMeters,
    int HandlingMinutes,
    int TransitMinutes,
    int TotalMinutes,
    decimal EstimatedCostAmount,
    int MaxServingDistanceMeters,
    bool IsWithinServingDistance);
