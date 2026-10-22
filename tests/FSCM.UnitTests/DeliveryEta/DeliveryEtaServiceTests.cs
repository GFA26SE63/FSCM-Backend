using System.Text.Json;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.DeliveryEta;

public sealed class DeliveryEtaServiceTests
{
    // 2026-10-21 03:00 UTC is 10:00 in Vietnam, two hours before the 12:00 allocation run.
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Eta_waits_for_the_run_approval_picking_and_the_next_dispatch_slot()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None);

        var local = TimeZoneInfo.ConvertTime(result.EstimatedArrivalAt, Fixture.Vietnam);
        // 10:00 -> run 12:00 -> approved 14:00 -> picked 18:00 -> loaded 19:00
        // -> next slot 06:00 next day -> +120 transit -> +60 buffer -> 09:00 on the 22nd.
        Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), local.DateTime);
        Assert.Equal(120, result.Breakdown.WaitForRunMinutes);
        Assert.Equal(120, result.Breakdown.ApprovalMinutes);
        Assert.Equal(240, result.Breakdown.PreparationMinutes);
        Assert.Equal(60, result.Breakdown.HandlingMinutes);
        Assert.Equal(660, result.Breakdown.WaitForDispatchSlotMinutes);
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
    public async Task An_order_arriving_after_the_cutoff_waits_for_the_next_day_run()
    {
        var fixture = await Fixture.CreateAsync();
        // 06:00 UTC is 13:00 in Vietnam, one hour after the run has already started.
        var afterCutoff = new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero);

        var result = await fixture.Service.EstimateAsync(fixture.Request(afterCutoff), CancellationToken.None);

        var runLocal = TimeZoneInfo.ConvertTime(result.Breakdown.AllocationRunAt, Fixture.Vietnam);
        Assert.Equal(new DateTime(2026, 10, 22, 12, 0, 0), runLocal.DateTime);
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
    public async Task A_schedule_whose_approval_deadline_precedes_the_run_is_rejected()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Store.ScheduleConfiguration!.ConfigValue =
            Fixture.ScheduleJson(runLocalTime: "14:00", approvalDeadlineLocalTime: "12:00");

        var exception = await Assert.ThrowsAsync<DeliveryEtaOperationException>(() =>
            fixture.Service.EstimateAsync(fixture.Request(), CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
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
            string runLocalTime = "12:00", string approvalDeadlineLocalTime = "14:00") =>
            JsonSerializer.Serialize(new
            {
                timeZone = "Asia/Ho_Chi_Minh",
                allocationRunLocalTime = runLocalTime,
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
