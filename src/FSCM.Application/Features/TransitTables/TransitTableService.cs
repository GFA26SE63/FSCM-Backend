using System.Text.Json;
using FSCM.Application.Common;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.TransitTables;

public static class TransitTableAuditActions
{
    public const string Published = "Published";
}

/// <summary>
/// The shared transit table: one set of distance bands used by every warehouse. Publishing an
/// edit closes the current version and creates the next one, so an allocation run can still be
/// explained with the version it actually used.
/// </summary>
public sealed class TransitTableService(ITransitTableStore store, TimeProvider timeProvider)
{
    private const string EntityName = nameof(TransitTable);

    public async Task<TransitTableResult> GetCurrentAsync(CancellationToken cancellationToken) =>
        ToResult(await store.FindCurrentAsync(cancellationToken)
            ?? throw TransitTableOperationException.NotFound("No transit table has been published yet."));

    public async Task<TransitTableResult> GetVersionAsync(int version, CancellationToken cancellationToken) =>
        ToResult(await store.FindByVersionAsync(version, cancellationToken)
            ?? throw TransitTableOperationException.NotFound($"Transit table version {version} was not found."));

    public async Task<IReadOnlyCollection<TransitTableResult>> ListHistoryAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).Select(ToResult).ToArray();

    /// <summary>
    /// The band serving the given distance, from the version in force at <paramref name="at"/>
    /// when given, otherwise the current one. Used by the shared ETA function and by delivery
    /// cost estimation.
    /// </summary>
    public async Task<TransitLookupResult> LookupAsync(
        int distanceMeters, DateTimeOffset? at, CancellationToken cancellationToken)
    {
        if (distanceMeters < 0)
        {
            throw TransitTableOperationException.Validation("Distance cannot be negative.");
        }

        var moment = at ?? timeProvider.GetUtcNow();
        var view = await store.FindEffectiveAtAsync(moment, cancellationToken)
            ?? throw TransitTableOperationException.NotFound($"No transit table was in force at {moment:O}.");

        var band = view.Bands.FirstOrDefault(item =>
            distanceMeters >= item.MinDistanceMeters && distanceMeters < item.MaxDistanceMeters)
            // Serving radius is inclusive. If it is the table's final endpoint, it belongs to
            // the final band; ordinary shared boundaries still resolve to the following band.
            ?? (distanceMeters == view.Table.MaxServingDistanceMeters &&
                view.Bands.Max(item => item.MaxDistanceMeters) == distanceMeters
                ? view.Bands.FirstOrDefault(item => item.MaxDistanceMeters == distanceMeters) : null)
            ?? throw TransitTableOperationException.NotFound(
                $"Transit table version {view.Table.Version} has no band covering {distanceMeters} m.");

        return new TransitLookupResult(
            view.Table.Id,
            view.Table.Version,
            distanceMeters,
            band.MinDistanceMeters,
            band.MaxDistanceMeters,
            band.HandlingMinutes,
            band.TransitMinutes,
            band.HandlingMinutes + band.TransitMinutes,
            band.EstimatedCostAmount,
            view.Table.MaxServingDistanceMeters,
            distanceMeters <= view.Table.MaxServingDistanceMeters);
    }

    public async Task<TransitTableResult> PublishAsync(
        PublishTransitTableData data, Guid actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bands = Validate(data);
        var now = timeProvider.GetUtcNow();
        if (data.EffectiveFrom < EffectiveDates.Tomorrow(now))
            throw TransitTableOperationException.Validation(
                "A new transit table can take effect no earlier than tomorrow (Vietnam time).");

        var current = await store.FindCurrentForUpdateAsync(cancellationToken);
        if (current is not null)
        {
            if (data.EffectiveFrom <= current.EffectiveFrom)
            {
                throw TransitTableOperationException.Conflict(
                    "A new transit table must take effect after the current one.");
            }

            current.IsActive = false;
            current.EffectiveTo = data.EffectiveFrom.ToUniversalTime();
            current.UpdatedAt = now;
        }

        var table = new TransitTable
        {
            Id = Guid.NewGuid(),
            Version = await store.MaxVersionAsync(cancellationToken) + 1,
            MaxServingDistanceMeters = data.MaxServingDistanceMeters,
            EffectiveFrom = data.EffectiveFrom.ToUniversalTime(),
            EffectiveTo = null,
            IsActive = true,
            Note = data.Note?.Trim(),
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddTable(table);
        store.AddBands(bands.Select(item => new TransitTableBand
        {
            Id = Guid.NewGuid(),
            TransitTableId = table.Id,
            MinDistanceMeters = item.MinDistanceMeters,
            MaxDistanceMeters = item.MaxDistanceMeters,
            HandlingMinutes = item.HandlingMinutes,
            TransitMinutes = item.TransitMinutes,
            EstimatedCostAmount = item.EstimatedCostAmount,
            CreatedAt = now
        }));

        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = actorUserId,
            EntityName = EntityName,
            EntityId = table.Id,
            Action = TransitTableAuditActions.Published,
            OldValues = current is null
                ? null
                : JsonSerializer.Serialize(new { current.Version, current.EffectiveFrom }),
            NewValues = JsonSerializer.Serialize(new { table.Version, table.EffectiveFrom, BandCount = bands.Count }),
            Reason = table.Note,
            CreatedAt = now
        });

        await store.SaveChangesAsync(cancellationToken);
        return await GetVersionAsync(table.Version, cancellationToken);
    }

    /// <summary>
    /// Bands must start at zero and meet end to end with no gap and no overlap, so every distance
    /// inside the table resolves to exactly one band.
    /// </summary>
    private static IReadOnlyList<TransitBandData> Validate(PublishTransitTableData data)
    {
        if (data.MaxServingDistanceMeters <= 0)
        {
            throw TransitTableOperationException.Validation("Maximum serving distance must be greater than zero.");
        }

        if (data.Note?.Trim().Length > 500)
            throw TransitTableOperationException.Validation("Note cannot exceed 500 characters.");

        if (data.Bands.Count == 0)
        {
            throw TransitTableOperationException.Validation("A transit table needs at least one distance band.");
        }

        var bands = data.Bands.OrderBy(item => item.MinDistanceMeters).ToArray();
        if (bands[0].MinDistanceMeters != 0)
        {
            throw TransitTableOperationException.Validation("The first distance band must start at 0 m.");
        }

        for (var index = 0; index < bands.Length; index++)
        {
            var band = bands[index];
            if (band.MaxDistanceMeters <= band.MinDistanceMeters)
            {
                throw TransitTableOperationException.Validation(
                    $"Band starting at {band.MinDistanceMeters} m must end after it starts.");
            }

            if (band.HandlingMinutes < 0 || band.TransitMinutes < 0 || (long)band.HandlingMinutes + band.TransitMinutes > int.MaxValue)
            {
                throw TransitTableOperationException.Validation("Handling and transit minutes cannot be negative.");
            }

            if (band.EstimatedCostAmount < 0 || band.EstimatedCostAmount > 9999999999999999.99m || decimal.Round(band.EstimatedCostAmount, 2) != band.EstimatedCostAmount)
            {
                throw TransitTableOperationException.Validation("Estimated cost cannot be negative.");
            }

            if (index > 0 && band.MinDistanceMeters != bands[index - 1].MaxDistanceMeters)
            {
                throw TransitTableOperationException.Validation(
                    $"Distance bands must meet end to end: {bands[index - 1].MaxDistanceMeters} m then {band.MinDistanceMeters} m.");
            }
        }

        if (bands[^1].MaxDistanceMeters < data.MaxServingDistanceMeters)
        {
            throw TransitTableOperationException.Validation("Distance bands must cover the whole serving distance.");
        }

        return bands;
    }

    private static TransitTableResult ToResult(TransitTableView view) => new(
        view.Table.Id, view.Table.Version, view.Table.MaxServingDistanceMeters,
        view.Table.EffectiveFrom, view.Table.EffectiveTo, view.Table.IsActive, view.Table.Note,
        view.Table.CreatedByUserId, view.Table.CreatedAt, view.Table.UpdatedAt,
        view.Bands.OrderBy(item => item.MinDistanceMeters)
            .Select(item => new TransitBandResult(
                item.Id, item.MinDistanceMeters, item.MaxDistanceMeters,
                item.HandlingMinutes, item.TransitMinutes, item.EstimatedCostAmount))
            .ToArray());
}
