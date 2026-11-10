using System.Text.Json;
using FSCM.Application.Features.TransitTables;

namespace FSCM.Application.Features.DeliveryEta;

/// <summary>
/// The shared estimate of when the customer receives the goods. Allocation is real-time, so the
/// estimate starts at the supplied reference instant. It then includes preparation, warehouse
/// handling, the next dispatch slot, transit and the configured safety buffer.
/// </summary>
public sealed class DeliveryEtaService(
    IDeliveryEtaStore store,
    TransitTableService transitTables,
    TimeProvider timeProvider)
{
    private const string ScheduleConfigurationKey = "eta.schedule";

    // Configuration is authored as camelCase JSON, so bind without regard to case.
    private static readonly JsonSerializerOptions ScheduleJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private const string Disclaimer =
        "ETA chỉ là thời gian ước tính, không phải cam kết thời điểm giao hàng.";

    /// <summary>
    /// The currently published operational ETA schedule. Allocation itself has no scheduled
    /// milestone; the returned milestones are only the warehouse dispatch slots for the selected
    /// business date.
    /// </summary>
    public async Task<AllocationScheduleResult> GetScheduleAsync(
        DateOnly? businessDate, CancellationToken cancellationToken)
    {
        var view = await LoadScheduleAsync(cancellationToken);
        var schedule = view.Schedule;
        var zone = ResolveTimeZone(schedule.TimeZone);
        var date = businessDate ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);

        return new AllocationScheduleResult(
            date,
            schedule.TimeZone,
            view.Version,
            schedule.DispatchSlotsLocalTime.Select((slot, index) => new ScheduleMilestone(
                $"dispatch_slot_{index + 1}", $"Chuyến xuất kho {index + 1}", slot,
                ToOffset(date, slot, zone), false)).ToArray(),
            schedule.PreparationMinutes,
            schedule.BufferMinutes,
            schedule.DispatchSlotsLocalTime);
    }

    public async Task<EtaResult> EstimateAsync(EtaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var referenceAt = request.ReferenceAt ?? now;

        var scheduleView = await LoadScheduleAsync(cancellationToken);
        var schedule = scheduleView.Schedule;
        var zone = ResolveTimeZone(schedule.TimeZone);

        var distance = await store.FindDistanceAsync(request.WarehouseId, request.RetailerId, cancellationToken)
            ?? throw DeliveryEtaOperationException.NotFound(
                "No cached distance exists for this warehouse and Retailer.");

        await EnsureDistanceIsCurrentAsync(distance.OriginLocationVersion, "warehouse", request.WarehouseId, cancellationToken);
        await EnsureDistanceIsCurrentAsync(distance.DestinationLocationVersion, "retailer", request.RetailerId, cancellationToken);

        // The transit band is read as of the reference moment, so a re-check of an older run still
        // reads the table version that run actually used.
        var transit = await transitTables.LookupAsync(distance.DistanceMeters, referenceAt, cancellationToken);

        // The order is allocated and priced in the request transaction. These legacy breakdown
        // fields remain in the response for API compatibility, but both now point at the real-time
        // reference and contribute zero waiting minutes.
        var allocationRunAt = referenceAt;
        const int approvalMinutes = 0;
        var planApprovedAt = referenceAt;
        var pickingDoneAt = planApprovedAt.AddMinutes(schedule.PreparationMinutes);
        var readyToLoadAt = pickingDoneAt.AddMinutes(transit.HandlingMinutes);
        var departureAt = NextDispatchSlot(readyToLoadAt, schedule.DispatchSlotsLocalTime, zone);
        var estimatedArrivalAt = departureAt
            .AddMinutes(transit.TransitMinutes)
            .AddMinutes(schedule.BufferMinutes);

        var breakdown = new EtaBreakdown(
            referenceAt,
            allocationRunAt,
            planApprovedAt,
            pickingDoneAt,
            readyToLoadAt,
            departureAt,
            estimatedArrivalAt,
            0,
            approvalMinutes,
            schedule.PreparationMinutes,
            transit.HandlingMinutes,
            (int)(departureAt - readyToLoadAt).TotalMinutes,
            transit.TransitMinutes,
            schedule.BufferMinutes);

        return new EtaResult(
            request.WarehouseId,
            request.RetailerId,
            distance.DistanceMeters,
            transit.IsWithinServingDistance,
            transit.EstimatedCostAmount,
            breakdown,
            scheduleView.Version,
            transit.Version,
            distance.Version,
            Disclaimer);
    }

    /// <summary>
    /// The first occurrence of <paramref name="localTime"/> at or after <paramref name="from"/>.
    /// Compared in the schedule's own zone so a run at 12:00 Vietnam time is not read as 12:00 UTC.
    /// </summary>
    private static DateTimeOffset NextLocalTime(DateTimeOffset from, TimeOnly localTime, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(from, zone);
        var candidate = ToOffset(DateOnly.FromDateTime(local.DateTime), localTime, zone);
        return candidate >= from ? candidate : ToOffset(DateOnly.FromDateTime(local.DateTime).AddDays(1), localTime, zone);
    }

    private static DateTimeOffset NextDispatchSlot(
        DateTimeOffset from, IReadOnlyList<TimeOnly> slots, TimeZoneInfo zone) =>
        slots.Select(slot => NextLocalTime(from, slot, zone)).Min();

    /// <summary>
    /// The instant a milestone falls on, for a run whose cutoff is <paramref name="cutoff"/> on
    /// <paramref name="date"/>.
    ///
    /// The five milestones are stored as times of day, which is only enough while the cutoff sits
    /// in the middle of the working day. A cutoff at midnight closes the day that has just ended,
    /// so the advice and the review reminder belong to the previous calendar day while the approval
    /// milestones belong to the day the run is on. Placing each one relative to the cutoff rather
    /// than on a fixed date lets both arrangements be read off the same five times, and leaves a
    /// midday cutoff resolving exactly as it did before.
    /// </summary>
    private static DateTimeOffset ResolveMilestone(
        DateOnly date, TimeOnly cutoff, TimeOnly at, bool beforeCutoff, TimeZoneInfo zone)
    {
        var cutoffAt = ToOffset(date, cutoff, zone);
        var candidate = ToOffset(date, at, zone);
        if (beforeCutoff && candidate >= cutoffAt) return ToOffset(date.AddDays(-1), at, zone);
        // Strictly earlier, so the cutoff itself - which is passed through here too - stays put
        // instead of being pushed to the next day.
        if (!beforeCutoff && candidate < cutoffAt) return ToOffset(date.AddDays(1), at, zone);
        return candidate;
    }

    private static DateTimeOffset ToOffset(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    private async Task EnsureDistanceIsCurrentAsync(
        int cachedLocationVersion, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var location = await store.FindLocationAsync(entityType, entityId, cancellationToken)
            ?? throw DeliveryEtaOperationException.NotFound($"No {entityType} location is registered.");
        if (location.Version != cachedLocationVersion)
        {
            throw DeliveryEtaOperationException.Conflict(
                $"The cached distance is stale because the {entityType} location has changed.");
        }
    }

    private async Task<EtaScheduleView> LoadScheduleAsync(CancellationToken cancellationToken)
    {
        var configuration = await store.FindScheduleConfigurationAsync(cancellationToken)
            ?? throw DeliveryEtaOperationException.NotFound(
                $"Business configuration '{ScheduleConfigurationKey}' has not been published yet.");

        EtaScheduleDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<EtaScheduleDocument>(configuration.ConfigValue, ScheduleJsonOptions);
        }
        catch (JsonException exception)
        {
            throw DeliveryEtaOperationException.Validation(
                $"Business configuration '{ScheduleConfigurationKey}' is not valid JSON: {exception.Message}");
        }

        if (document is null)
        {
            throw DeliveryEtaOperationException.Validation(
                $"Business configuration '{ScheduleConfigurationKey}' is empty.");
        }

        return new EtaScheduleView(document.ToSchedule(), configuration.Version);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw DeliveryEtaOperationException.Validation($"Time zone '{id}' is not available on this host.");
        }
    }

    private sealed record EtaScheduleDocument(
        string? TimeZone,
        string? SalesSubmissionAdviceLocalTime,
        string? OrderReviewReminderLocalTime,
        string? AllocationRunLocalTime,
        string? PlanApprovalReminderLocalTime,
        string? PlanApprovalDeadlineLocalTime,
        int PreparationMinutes,
        int BufferMinutes,
        IReadOnlyList<string>? DispatchSlotsLocalTime)
    {
        // Legacy run/approval fields remain readable so existing configuration rows can be loaded
        // during rollout. They no longer gate allocation or contribute to ETA.
        private static readonly TimeOnly LegacyMidnight = new(0, 0);

        public EtaSchedule ToSchedule()
        {
            if (string.IsNullOrWhiteSpace(TimeZone))
            {
                throw DeliveryEtaOperationException.Validation("The ETA schedule needs a time zone.");
            }

            _ = ResolveTimeZone(TimeZone);
            var run = OptionalTime(AllocationRunLocalTime, nameof(AllocationRunLocalTime), LegacyMidnight);
            var deadline = OptionalTime(PlanApprovalDeadlineLocalTime, nameof(PlanApprovalDeadlineLocalTime), LegacyMidnight);
            var salesAdvice = OptionalTime(SalesSubmissionAdviceLocalTime, nameof(SalesSubmissionAdviceLocalTime), LegacyMidnight);
            var orderReminder = OptionalTime(OrderReviewReminderLocalTime, nameof(OrderReviewReminderLocalTime), LegacyMidnight);
            var planReminder = OptionalTime(PlanApprovalReminderLocalTime, nameof(PlanApprovalReminderLocalTime), LegacyMidnight);

            if (PreparationMinutes < 0 || BufferMinutes < 0)
            {
                throw DeliveryEtaOperationException.Validation(
                    "Preparation and buffer minutes cannot be negative.");
            }

            if (DispatchSlotsLocalTime is null || DispatchSlotsLocalTime.Count == 0)
            {
                throw DeliveryEtaOperationException.Validation("The ETA schedule needs at least one dispatch slot.");
            }

            return new EtaSchedule(
                TimeZone,
                salesAdvice,
                orderReminder,
                run,
                planReminder,
                deadline,
                PreparationMinutes,
                BufferMinutes,
                DispatchSlotsLocalTime
                    .Select(item => ParseTime(item, nameof(DispatchSlotsLocalTime)))
                    .Order()
                    .ToArray());
        }

        private static TimeOnly OptionalTime(string? value, string field, TimeOnly fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : ParseTime(value, field);

        private static TimeOnly ParseTime(string? value, string field) =>
            TimeOnly.TryParse(value, out var parsed)
                ? parsed
                : throw DeliveryEtaOperationException.Validation($"'{field}' must be a time such as 12:00.");
    }
}
