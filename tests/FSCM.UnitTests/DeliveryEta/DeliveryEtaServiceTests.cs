using System.Text.Json;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.DeliveryEta;

public sealed class DeliveryEtaServiceTests
{
    [Fact]
    public async Task Expired_route_cache_is_rejected_instead_of_promising_a_stale_ETA()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.Distance.ExpiresAt = Now.AddMinutes(-1);
        var error = await Assert.ThrowsAsync<DeliveryEtaOperationException>(() => fixture.Service.EstimateAsync(fixture.Request(), default));
        Assert.Equal("conflict", error.Code);
    }
    // 2026-10-21 03:00 UTC is 10:00 in Vietnam.
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Eta_starts_at_real_time_allocation_then_waits_for_preparation_and_dispatch()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None);

        var local = TimeZoneInfo.ConvertTime(result.EstimatedArrivalAt, Fixture.Vietnam);
        // Allocation is immediate at 10:00 -> picked 14:00 -> loaded 15:00
        // -> next slot 06:00 next day -> +120 transit -> +60 buffer -> 09:00 on the 22nd.
        Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), local.DateTime);
        Assert.Equal(Now, result.Breakdown.AllocationRunAt);
        Assert.Equal(Now, result.Breakdown.PlanApprovedAt);
        Assert.Equal(0, result.Breakdown.WaitForRunMinutes);
        Assert.Equal(0, result.Breakdown.ApprovalMinutes);
        Assert.Equal(240, result.Breakdown.PreparationMinutes);
        Assert.Equal(60, result.Breakdown.HandlingMinutes);
        Assert.Equal(900, result.Breakdown.WaitForDispatchSlotMinutes);
        Assert.Equal(120, result.Breakdown.TransitMinutes);
        Assert.Equal(60, result.Breakdown.BufferMinutes);
    }

    [Fact]
    public async Task Eta_is_far_later_than_driving_time_alone()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None);

        var drivingOnly = Now.AddMinutes(result.Breakdown.TransitMinutes);
        Assert.True(result.EstimatedArrivalAt > drivingOnly.AddHours(12),
            "The schedule stages must dominate the estimate, not the driving time.");
    }

    [Fact]
    public async Task Any_reference_time_allocates_immediately_without_a_cutoff()
    {
        var fixture = await Fixture.CreateAsync();
        // 06:00 UTC is 13:00 in Vietnam; the former daily cutoff no longer matters.
        var afterCutoff = new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero);

        var result = await fixture.Service.EstimateAsync(fixture.Request(afterCutoff), CancellationToken.None);

        Assert.Equal(afterCutoff, result.Breakdown.AllocationRunAt);
        Assert.Equal(0, result.Breakdown.WaitForRunMinutes);
    }

    [Fact]
    public async Task Eta_reports_the_schedule_transit_and_distance_versions_it_used()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(7, result.ScheduleVersion);
        Assert.Equal(1, result.TransitTableVersion);
        Assert.Equal(3, result.DistanceVersion);
        Assert.Contains("không phải cam kết", result.Disclaimer);
    }

    [Fact]
    public async Task Eta_rejects_a_cached_distance_after_a_location_moved()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.RetailerLocation.Version = 2;

        var exception = await Assert.ThrowsAsync<DeliveryEtaOperationException>(() =>
            fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Eta_beyond_the_serving_distance_is_reported_rather_than_rejected()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.Distance.DistanceMeters = 18_000;

        var result = await fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None);

        Assert.False(result.IsWithinServingDistance);
    }

    [Fact]
    public async Task Eta_without_a_published_schedule_reports_not_found()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration = null;

        var exception = await Assert.ThrowsAsync<DeliveryEtaOperationException>(() =>
            fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None));

        Assert.Equal("not_found", exception.Code);
    }

    [Fact]
    public async Task Legacy_run_and_approval_fields_no_longer_gate_eta()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue =
            Fixture.ScheduleJson(runLocalTime: "14:00", approvalDeadlineLocalTime: "14:00");

        var result = await fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None);

        Assert.Equal(0, result.Breakdown.WaitForRunMinutes);
        Assert.Equal(0, result.Breakdown.ApprovalMinutes);
    }

    [Fact]
    public async Task Schedule_exposes_only_dispatch_slots_for_the_business_date()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue = Fixture.ScheduleJson(runLocalTime: "00:00");

        var schedule = await fixture.Service.GetScheduleAsync(
            new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal(["dispatch_slot_1", "dispatch_slot_2"],
            schedule.Milestones.Select(item => item.Key));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.FromHours(7)),
            schedule.Milestones[0].At);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.FromHours(7)),
            schedule.Milestones[1].At);
    }

    [Fact]
    public async Task A_midnight_cutoff_still_keeps_the_timetable_in_order()
    {
        var fixture = await Fixture.CreateAsync();

        var schedule = await fixture.Service.GetScheduleAsync(
            new DateOnly(2026, 10, 26), CancellationToken.None);
        var midday = schedule.Milestones.Select(item => item.At).ToArray();

        fixture.Store.ScheduleConfiguration!.ConfigValue = Fixture.ScheduleJson(runLocalTime: "00:00");
        var midnight = (await fixture.Service.GetScheduleAsync(
            new DateOnly(2026, 10, 26), CancellationToken.None)).Milestones.Select(item => item.At).ToArray();

        // Whatever hour the day closes at, the five instants only ever move forwards.
        foreach (var instants in new[] { midday, midnight })
        {
            Assert.Equal(instants.OrderBy(item => item).ToArray(), instants);
        }
    }

    [Fact]
    public async Task A_midday_cutoff_keeps_every_milestone_on_its_own_business_date()
    {
        var fixture = await Fixture.CreateAsync();

        var schedule = await fixture.Service.GetScheduleAsync(
            new DateOnly(2026, 10, 26), CancellationToken.None);

        // Placing the milestones around the cutoff must not disturb the arrangement that was
        // already in use: at midday, all five still fall on the business date itself.
        Assert.All(schedule.Milestones, item =>
            Assert.Equal(new DateOnly(2026, 10, 26), DateOnly.FromDateTime(item.At.DateTime)));
    }

    [Fact]
    public async Task Legacy_reminder_times_are_ignored()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue = Fixture.ScheduleJson(
            runLocalTime: "00:00", orderReviewReminderLocalTime: "00:30");

        var schedule = await fixture.Service.GetScheduleAsync(null, CancellationToken.None);

        Assert.Equal(2, schedule.Milestones.Count);
    }

    [Fact]
    public async Task Dispatch_timetable_comes_back_in_order()
    {
        var fixture = await Fixture.CreateAsync();

        var schedule = await fixture.Service.GetScheduleAsync(new DateOnly(2026, 10, 21), CancellationToken.None);

        Assert.Equal(
            ["dispatch_slot_1", "dispatch_slot_2"],
            schedule.Milestones.Select(item => item.Key));
        Assert.Equal(
            [new TimeOnly(6, 0), new TimeOnly(14, 0)],
            schedule.Milestones.Select(item => item.LocalTime));
        Assert.All(schedule.Milestones, item => Assert.False(item.IsAdvisory));
    }

    [Fact]
    public async Task Each_milestone_is_resolved_to_an_instant_in_the_published_time_zone()
    {
        var fixture = await Fixture.CreateAsync();

        var schedule = await fixture.Service.GetScheduleAsync(new DateOnly(2026, 10, 21), CancellationToken.None);

        var firstDispatch = schedule.Milestones.Single(item => item.Key == "dispatch_slot_1");
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.FromHours(7)), firstDispatch.At);
        Assert.Equal("Asia/Ho_Chi_Minh", schedule.TimeZone);
    }

    [Fact]
    public async Task A_schedule_with_legacy_fields_still_loads()
    {
        var fixture = await Fixture.CreateAsync();

        var schedule = await fixture.Service.GetScheduleAsync(new DateOnly(2026, 10, 21), CancellationToken.None);

        Assert.Equal(new TimeOnly(6, 0), schedule.Milestones[0].LocalTime);
        Assert.Equal(new TimeOnly(14, 0), schedule.Milestones[1].LocalTime);
    }

    [Fact]
    public async Task A_legacy_order_reminder_does_not_change_dispatch_configuration()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue = Fixture.ScheduleJson(orderReviewReminderLocalTime: "12:30");

        var schedule = await fixture.Service.GetScheduleAsync(null, CancellationToken.None);

        Assert.Equal([new TimeOnly(6, 0), new TimeOnly(14, 0)],
            schedule.Milestones.Select(item => item.LocalTime));
    }

    [Fact]
    public async Task A_legacy_plan_reminder_does_not_change_dispatch_configuration()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue = Fixture.ScheduleJson(planApprovalReminderLocalTime: "15:00");

        var schedule = await fixture.Service.GetScheduleAsync(null, CancellationToken.None);

        Assert.Equal(2, schedule.Milestones.Count);
    }

    [Fact]
    public async Task Legacy_custom_milestones_do_not_replace_dispatch_slots()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue = Fixture.ScheduleJson(
            runLocalTime: "10:00",
            approvalDeadlineLocalTime: "12:00",
            salesAdviceLocalTime: "08:30",
            orderReviewReminderLocalTime: "09:30",
            planApprovalReminderLocalTime: "11:00");

        var schedule = await fixture.Service.GetScheduleAsync(new DateOnly(2026, 10, 21), CancellationToken.None);

        Assert.Equal(
            [new TimeOnly(6, 0), new TimeOnly(14, 0)],
            schedule.Milestones.Select(item => item.LocalTime));
    }

    private sealed class Fixture
    {
        public static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

        public Guid WarehouseId { get; } = Guid.NewGuid();
        public Guid RetailerId { get; } = Guid.NewGuid();
        public FakeStore Store { get; private set; } = null!;
        public DeliveryEtaService Service { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var transitStore = new FakeTransitTableStore();
            var transitTables = new TransitTableService(transitStore, new FixedClock());
            // Bands reach further than the serving distance, so a distance past 15 km still
            // resolves to a band and is reported as out of range instead of failing.
            await transitTables.PublishAsync(
                new PublishTransitTableData(15_000, Now.AddDays(-30), null,
                [
                    new TransitBandData(0, 10_000, 60, 120, 80_000m),
                    new TransitBandData(10_000, 20_000, 60, 240, 120_000m)
                ]),
                Actor,
                CancellationToken.None);

            fixture.Store = new FakeStore(fixture.WarehouseId, fixture.RetailerId);
            fixture.Service = new DeliveryEtaService(fixture.Store, transitTables, new FixedClock());
            return fixture;
        }

        public EtaRequest Request(DateTimeOffset? referenceAt = null) =>
            new(WarehouseId, RetailerId, referenceAt ?? Now);

        public static string ScheduleJson(
            string runLocalTime = "12:00",
            string approvalDeadlineLocalTime = "14:00",
            string? salesAdviceLocalTime = null,
            string? orderReviewReminderLocalTime = null,
            string? planApprovalReminderLocalTime = null) =>
            JsonSerializer.Serialize(new
            {
                timeZone = "Asia/Ho_Chi_Minh",
                salesSubmissionAdviceLocalTime = salesAdviceLocalTime,
                orderReviewReminderLocalTime,
                allocationRunLocalTime = runLocalTime,
                planApprovalReminderLocalTime,
                planApprovalDeadlineLocalTime = approvalDeadlineLocalTime,
                preparationMinutes = 240,
                bufferMinutes = 60,
                dispatchSlotsLocalTime = new[] { "06:00", "14:00" }
            });
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeStore : IDeliveryEtaStore
    {
        public FakeStore(Guid warehouseId, Guid retailerId)
        {
            WarehouseLocation = new MapLocation { WarehouseId = warehouseId, Version = 1 };
            RetailerLocation = new MapLocation { RetailerId = retailerId, Version = 1 };
            Distance = new MapDistanceCache
            {
                WarehouseId = warehouseId,
                RetailerId = retailerId,
                OriginLocationVersion = 1,
                DestinationLocationVersion = 1,
                DistanceMeters = 5_000,
                DurationSeconds = 900,
                Version = 3,
                ExpiresAt = Now.AddHours(6),
                IsActive = true
            };
            ScheduleConfiguration = new SystemConfiguration
            {
                ConfigKey = "eta.schedule",
                ConfigValue = Fixture.ScheduleJson(),
                Version = 7,
                IsActive = true
            };
        }

        public MapLocation WarehouseLocation { get; }
        public MapLocation RetailerLocation { get; }
        public MapDistanceCache Distance { get; }
        public SystemConfiguration? ScheduleConfiguration { get; set; }

        public Task<SystemConfiguration?> FindScheduleConfigurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ScheduleConfiguration);

        public Task<MapDistanceCache?> FindDistanceAsync(
            Guid warehouseId, Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult<MapDistanceCache?>(Distance);

        public Task<MapLocation?> FindLocationAsync(
            string entityType, Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<MapLocation?>(entityType == "warehouse" ? WarehouseLocation : RetailerLocation);
    }

    private sealed class FakeTransitTableStore : ITransitTableStore
    {
        private readonly List<TransitTable> _tables = [];
        private readonly List<TransitTableBand> _bands = [];

        public Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.IsActive)));

        public Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.Version == version)));

        public Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item =>
                item.EffectiveFrom <= at && (item.EffectiveTo is null || item.EffectiveTo > at))));

        public Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<TransitTableView>>(
                _tables.Select(item => ToView(item)!).ToArray());

        public Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.SingleOrDefault(item => item.IsActive));

        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.Count == 0 ? 0 : _tables.Max(item => item.Version));

        public void AddTable(TransitTable table) => _tables.Add(table);

        public void AddBands(IEnumerable<TransitTableBand> bands) => _bands.AddRange(bands);

        public void AddAuditLog(AuditLog auditLog) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private TransitTableView? ToView(TransitTable? table) => table is null
            ? null
            : new TransitTableView(table, _bands
                .Where(item => item.TransitTableId == table.Id)
                .OrderBy(item => item.MinDistanceMeters)
                .ToArray());
    }
}
