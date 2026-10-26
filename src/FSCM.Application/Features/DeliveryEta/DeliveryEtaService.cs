using System.Text.Json;
using FSCM.Application.Features.TransitTables;

namespace FSCM.Application.Features.DeliveryEta;

/// <summary>
/// The shared estimate of when the customer receives the goods. An order placed today is not
/// driven to the retailer immediately: it waits for the day's allocation run, waits for the plan
/// to be approved, is picked, waits for the next dispatch slot, and only then travels. Adding
/// driving time to "now" would understate the arrival by a day or more, so every stage is
/// modelled and reported.
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
    /// The run-day timetable for one business date.
    ///
    /// The cutoff and the approval deadline already lived here because the ETA is built from them;
    /// the reminders join them rather than going into a configuration of their own, so the portal,
    /// the ETA and the job cannot end up disagreeing about when the day runs.
    ///
    /// The sales advice is marked advisory on purpose. An order accepted after it and before the
    /// cutoff is still in the run, and a caller that reads it as a deadline would drop valid orders.
    /// </summary>
    public async Task<AllocationScheduleResult> GetScheduleAsync(
        DateOnly? businessDate, CancellationToken cancellationToken)
    {
        var view = await LoadScheduleAsync(cancellationToken);
        var schedule = view.Schedule;
        var zone = ResolveTimeZone(schedule.TimeZone);
        var date = businessDate ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);

        var cutoff = schedule.AllocationRunLocalTime;
        ScheduleMilestone Milestone(string key, string label, TimeOnly at, bool advisory, bool beforeCutoff) =>
            new(key, label, at, ResolveMilestone(date, cutoff, at, beforeCutoff, zone), advisory);

        return new AllocationScheduleResult(
            date,
            schedule.TimeZone,
            view.Version,
            [
                Milestone("sales_submission_advice", "Khuyến nghị Sales gửi đơn trước",
                    schedule.SalesSubmissionAdviceLocalTime, true, true),
                Milestone("order_review_reminder", "Nhắc duyệt đơn còn chờ",
                    schedule.OrderReviewReminderLocalTime, false, true),
                Milestone("allocation_run", "Giờ chốt đợt",
                    schedule.AllocationRunLocalTime, false, false),
                Milestone("plan_approval_reminder", "Nhắc duyệt phương án",
                    schedule.PlanApprovalReminderLocalTime, false, false),
                Milestone("plan_approval_deadline", "Hạn duyệt phương án",
                    schedule.PlanApprovalDeadlineLocalTime, false, false)
            ],
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

        var allocationRunAt = NextLocalTime(referenceAt, schedule.AllocationRunLocalTime, zone);
        var approvalMinutes = (int)(schedule.PlanApprovalDeadlineLocalTime - schedule.AllocationRunLocalTime).TotalMinutes;
        var planApprovedAt = allocationRunAt.AddMinutes(approvalMinutes);
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
            (int)(allocationRunAt - referenceAt).TotalMinutes,
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
        // The reminders were added after the schedule was first published, so a document without
        // them is still valid and falls back to the recommended times rather than failing to load.
        private static readonly TimeOnly DefaultSalesAdvice = new(11, 0);
        private static readonly TimeOnly DefaultOrderReviewReminder = new(11, 30);
        private static readonly TimeOnly DefaultPlanApprovalReminder = new(13, 30);

        public EtaSchedule ToSchedule()
        {
            if (string.IsNullOrWhiteSpace(TimeZone))
            {
                throw DeliveryEtaOperationException.Validation("The ETA schedule needs a time zone.");
            }

            var run = ParseTime(AllocationRunLocalTime, nameof(AllocationRunLocalTime));
            var deadline = ParseTime(PlanApprovalDeadlineLocalTime, nameof(PlanApprovalDeadlineLocalTime));
            var salesAdvice = OptionalTime(
                SalesSubmissionAdviceLocalTime, nameof(SalesSubmissionAdviceLocalTime), DefaultSalesAdvice);
            var orderReminder = OptionalTime(
                OrderReviewReminderLocalTime, nameof(OrderReviewReminderLocalTime), DefaultOrderReviewReminder);
            var planReminder = OptionalTime(
                PlanApprovalReminderLocalTime, nameof(PlanApprovalReminderLocalTime), DefaultPlanApprovalReminder);

            // Ordered as instants, not as times of day.
            //
            // Comparing the raw times only works while the cutoff sits in the middle of the working
            // day. A cutoff at midnight has nothing before it on its own date, so a rule written as
            // "the advice must be earlier in the day than the cutoff" rejects every schedule that
            // closes the day at 00:00 - including one whose advice sits perfectly sensibly at 11:00
            // the morning before. Resolving each milestone around the cutoff first states the rule
            // that was always meant: the reminders come before the thing they remind about.
            var zone = ResolveTimeZone(TimeZone);
            // Any date: the check is about order, and these milestones carry no date of their own.
            var reference = new DateOnly(2000, 1, 3);
            var runAt = ToOffset(reference, run, zone);
            var adviceAt = ResolveMilestone(reference, run, salesAdvice, true, zone);
            var orderReminderAt = ResolveMilestone(reference, run, orderReminder, true, zone);
            var planReminderAt = ResolveMilestone(reference, run, planReminder, false, zone);
            var deadlineAt = ResolveMilestone(reference, run, deadline, false, zone);

            if (adviceAt >= orderReminderAt)
            {
                throw DeliveryEtaOperationException.Validation(
                    "The sales advice must come before the order review reminder.");
            }

            if (adviceAt >= runAt || orderReminderAt >= runAt)
            {
                throw DeliveryEtaOperationException.Validation(
                    "The sales advice and the order review reminder must both fall before the cutoff.");
            }

            if (planReminderAt <= runAt || planReminderAt >= deadlineAt)
            {
                throw DeliveryEtaOperationException.Validation(
                    "The plan approval reminder must fall between the cutoff and the approval deadline.");
            }

            if (deadlineAt <= runAt)
            {
                throw DeliveryEtaOperationException.Validation(
                    "The plan approval deadline must come after the allocation run.");
            }

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
