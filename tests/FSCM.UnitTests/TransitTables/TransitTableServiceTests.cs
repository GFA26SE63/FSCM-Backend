using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.TransitTables;

public sealed class TransitTableServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Publish_stores_bands_cost_and_first_version()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.PublishAsync(Table(), Actor, CancellationToken.None);

        Assert.Equal(1, result.Version);
        Assert.True(result.IsActive);
        Assert.Null(result.EffectiveTo);
        Assert.Equal(20_000, result.MaxServingDistanceMeters);
        Assert.Equal(2, result.Bands.Count);
        Assert.Equal(120_000m, result.Bands.Last().EstimatedCostAmount);
        Assert.Single(fixture.Store.AuditLogs);
    }

    [Fact]
    public async Task Publish_closes_the_previous_version_instead_of_overwriting_it()
    {
        var fixture = new Fixture();
        await fixture.Service.PublishAsync(Table(), Actor, CancellationToken.None);

        var second = await fixture.Service.PublishAsync(
            Table(effectiveFrom: Now.AddDays(7)), Actor, CancellationToken.None);

        Assert.Equal(2, second.Version);
        Assert.Equal(2, fixture.Store.Tables.Count);
        var first = fixture.Store.Tables.Single(item => item.Version == 1);
        Assert.False(first.IsActive);
        Assert.Equal(Now.AddDays(7), first.EffectiveTo);
    }

    [Fact]
    public async Task Lookup_at_a_past_moment_returns_the_version_that_was_in_force()
    {
        var fixture = new Fixture();
        await fixture.Service.PublishAsync(Table(), Actor, CancellationToken.None);
        await fixture.Service.PublishAsync(
            Table(effectiveFrom: Now.AddDays(7), transitMinutes: 2_880), Actor, CancellationToken.None);

        fixture.Clock.UtcNow = Now.AddDays(7);
        var duringFirst = await fixture.Service.LookupAsync(5_000, Now.AddDays(1), CancellationToken.None);
        var current = await fixture.Service.LookupAsync(5_000, null, CancellationToken.None);

        Assert.Equal(1, duringFirst.Version);
        Assert.Equal(1_440, duringFirst.TransitMinutes);
        Assert.Equal(2, current.Version);
        Assert.Equal(2_880, current.TransitMinutes);
    }

    [Fact]
    public async Task Lookup_treats_the_band_upper_bound_as_exclusive()
    {
        var fixture = new Fixture();
        await fixture.Service.PublishAsync(Table(), Actor, CancellationToken.None);

        fixture.Clock.UtcNow = Now;
        var inFirstBand = await fixture.Service.LookupAsync(9_999, null, CancellationToken.None);
        var onBoundary = await fixture.Service.LookupAsync(10_000, null, CancellationToken.None);

        Assert.Equal(0, inFirstBand.MinDistanceMeters);
        Assert.Equal(10_000, onBoundary.MinDistanceMeters);
        Assert.Equal(1_500, inFirstBand.TotalMinutes);
    }

    [Fact]
    public async Task Lookup_beyond_the_serving_distance_reports_it_without_failing()
    {
        var fixture = new Fixture();
        await fixture.Service.PublishAsync(
            Table(maxServingDistanceMeters: 15_000), Actor, CancellationToken.None);

        fixture.Clock.UtcNow = Now;
        var result = await fixture.Service.LookupAsync(18_000, null, CancellationToken.None);

        Assert.False(result.IsWithinServingDistance);
        Assert.Equal(15_000, result.MaxServingDistanceMeters);
    }

    [Theory]
    [InlineData(0, 10_000, 10_000, 20_000, "")]
    [InlineData(0, 12_000, 10_000, 20_000, "overlap")]
    [InlineData(0, 8_000, 10_000, 20_000, "gap")]
    [InlineData(2_000, 10_000, 10_000, 20_000, "does not start at zero")]
    public async Task Publish_requires_bands_that_start_at_zero_and_meet_end_to_end(
        int firstMin, int firstMax, int secondMin, int secondMax, string invalidReason)
    {
        var fixture = new Fixture();
        var data = new PublishTransitTableData(20_000, Now, null,
        [
            new TransitBandData(firstMin, firstMax, 60, 1_440, 80_000m),
            new TransitBandData(secondMin, secondMax, 60, 2_880, 120_000m)
        ]);

        if (invalidReason.Length == 0)
        {
            var result = await fixture.Service.PublishAsync(data, Actor, CancellationToken.None);
            Assert.Equal(2, result.Bands.Count);
            return;
        }

        var exception = await Assert.ThrowsAsync<TransitTableOperationException>(() =>
            fixture.Service.PublishAsync(data, Actor, CancellationToken.None));
        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Tables);
    }

    [Fact]
    public async Task Publish_rejects_bands_that_leave_part_of_the_serving_distance_uncovered()
    {
        var fixture = new Fixture();
        var data = new PublishTransitTableData(20_000, Now, null,
            [new TransitBandData(0, 10_000, 60, 1_440, 80_000m)]);

        var exception = await Assert.ThrowsAsync<TransitTableOperationException>(() =>
            fixture.Service.PublishAsync(data, Actor, CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Publish_rejects_an_effective_date_at_or_before_the_current_version()
    {
        var fixture = new Fixture();
        await fixture.Service.PublishAsync(Table(), Actor, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TransitTableOperationException>(() =>
            fixture.Service.PublishAsync(Table(), Actor, CancellationToken.None));

        Assert.Equal("conflict", exception.Code);
        Assert.Single(fixture.Store.Tables);
    }

    [Fact]
    public async Task Lookup_without_any_published_table_reports_not_found()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<TransitTableOperationException>(() =>
            fixture.Service.LookupAsync(5_000, null, CancellationToken.None));

        Assert.Equal("not_found", exception.Code);
    }

    [Fact]
    public async Task Publish_uses_Vietnam_midnight_and_does_not_close_current_on_invalid_date()
    {
        var f = new Fixture();
        f.Clock.UtcNow = DateTimeOffset.Parse("2026-10-21T17:01:00Z"); // Already October 22 in Vietnam.
        var rejected = await Assert.ThrowsAsync<TransitTableOperationException>(() => f.Service.PublishAsync(
            Table(DateTimeOffset.Parse("2026-10-22T16:59:59Z")), Actor, CancellationToken.None));
        Assert.Equal("validation_error", rejected.Code);
        Assert.Empty(f.Store.Tables);
        var accepted = await f.Service.PublishAsync(Table(DateTimeOffset.Parse("2026-10-22T17:00:00Z")), Actor, CancellationToken.None);
        Assert.Equal(1, accepted.Version);
        await Assert.ThrowsAsync<TransitTableOperationException>(() => f.Service.PublishAsync(Table(f.Clock.UtcNow), Actor, CancellationToken.None));
        Assert.True(f.Store.Tables.Single().IsActive);
        Assert.Null(f.Store.Tables.Single().EffectiveTo);
    }

    [Fact]
    public async Task Lookup_without_date_keeps_using_effective_version_while_new_version_is_scheduled()
    {
        var f = new Fixture();
        await f.Service.PublishAsync(Table(), Actor, CancellationToken.None);
        f.Clock.UtcNow = Now;
        await f.Service.PublishAsync(Table(Now.AddDays(7), transitMinutes: 2880), Actor, CancellationToken.None);
        Assert.Equal(1, (await f.Service.LookupAsync(1000, null, CancellationToken.None)).Version);
        f.Clock.UtcNow = Now.AddDays(7);
        Assert.Equal(2, (await f.Service.LookupAsync(1000, null, CancellationToken.None)).Version);
    }

    [Fact]
    public async Task Exact_serving_radius_is_covered_by_final_band()
    {
        var f = new Fixture();
        await f.Service.PublishAsync(Table(), Actor, default);
        f.Clock.UtcNow = Now;
        var edge = await f.Service.LookupAsync(20000, null, default);
        Assert.True(edge.IsWithinServingDistance);
        Assert.Equal(10000, edge.MinDistanceMeters);
    }

    private static PublishTransitTableData Table(
        DateTimeOffset? effectiveFrom = null,
        int maxServingDistanceMeters = 20_000,
        int transitMinutes = 1_440) =>
        new(maxServingDistanceMeters, effectiveFrom ?? Now, "Bảng vận chuyển dùng chung",
        [
            new TransitBandData(0, 10_000, 60, transitMinutes, 80_000m),
            new TransitBandData(10_000, 20_000, 60, transitMinutes * 2, 120_000m)
        ]);

    private sealed class Fixture
    {
        public FakeStore Store { get; } = new();
        public FakeClock Clock { get; } = new();
        public TransitTableService Service { get; }

        public Fixture() => Service = new TransitTableService(Store, Clock);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now.AddDays(-1);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeStore : ITransitTableStore
    {
        public List<TransitTable> Tables { get; } = [];
        public List<TransitTableBand> Bands { get; } = [];
        public List<AuditLog> AuditLogs { get; } = [];

        public Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Tables.SingleOrDefault(item => item.IsActive)));

        public Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Tables.SingleOrDefault(item => item.Version == version)));

        public Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Tables.SingleOrDefault(item =>
                item.EffectiveFrom <= at && (item.EffectiveTo is null || item.EffectiveTo > at))));

        public Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<TransitTableView>>(
                Tables.OrderByDescending(item => item.Version).Select(item => ToView(item)!).ToArray());

        public Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Tables.SingleOrDefault(item => item.IsActive));

        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Tables.Count == 0 ? 0 : Tables.Max(item => item.Version));

        public void AddTable(TransitTable table) => Tables.Add(table);

        public void AddBands(IEnumerable<TransitTableBand> bands) => Bands.AddRange(bands);

        public void AddAuditLog(AuditLog auditLog) => AuditLogs.Add(auditLog);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private TransitTableView? ToView(TransitTable? table) => table is null
            ? null
            : new TransitTableView(table, Bands
                .Where(item => item.TransitTableId == table.Id)
                .OrderBy(item => item.MinDistanceMeters)
                .ToArray());
    }
}
