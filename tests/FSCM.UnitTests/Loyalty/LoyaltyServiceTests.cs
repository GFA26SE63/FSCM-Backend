using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Loyalty;

public sealed class LoyaltyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_finishes_each_retailer_query_before_starting_another(bool includeInactive)
    {
        var fixture = new Fixture();
        var first = fixture.Store.AddProgram(Now, Now.AddDays(30));
        var second = fixture.Store.AddProgram(Now, Now.AddDays(60));
        var inactive = fixture.Store.AddProgram(Now, Now.AddDays(90));
        first.IsActive = second.IsActive = true;
        var retailerIds = fixture.Store.Programs.ToDictionary(program => program.Id, _ => Guid.NewGuid());
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readsStarted = 0;
        var activeReads = 0;
        var overlappingReads = 0;
        fixture.Store.ReadProgramRetailers = async (id, cancellationToken) =>
        {
            Interlocked.Increment(ref readsStarted);
            if (Interlocked.Increment(ref activeReads) > 1)
                Interlocked.Increment(ref overlappingReads);
            started.TrySetResult(true);
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
                return new[] { retailerIds[id] };
            }
            finally { Interlocked.Decrement(ref activeReads); }
        };

        var listing = fixture.Service.ListAsync(includeInactive, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var readsBeforeRelease = readsStarted;
        gate.SetResult(true);
        var rows = (await listing).ToArray();

        Assert.Equal(1, readsBeforeRelease);
        Assert.Equal(0, overlappingReads);
        var expected = includeInactive ? new[] { first, second, inactive } : new[] { first, second };
        Assert.Equal(expected.Select(program => program.Id), rows.Select(row => row.Id));
        foreach (var row in rows)
            Assert.Equal(new[] { retailerIds[row.Id] }, row.RetailerIds);
    }

    [Fact]
    public async Task Create_generates_draft_program_with_normalized_values()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.CreateAsync(
            new("  Tích điểm mùa lễ  ", "  Mô tả  ", 100_000m, 50,
                Now, Now.AddDays(30), Now.AddDays(365), [fixture.RetailerId]), fixture.ActorId, CancellationToken.None);

        Assert.Equal("LP-001", result.Code);
        Assert.Equal("Tích điểm mùa lễ", result.Name);
        Assert.Equal("Mô tả", result.Description);
        Assert.False(result.IsActive);
        Assert.Single(fixture.Store.Programs);
    }

    [Fact]
    public async Task Expired_program_cannot_be_activated()
    {
        var fixture = new Fixture();
        var program = fixture.Store.AddProgram(Now.AddDays(-5), Now.AddDays(-1));

        var error = await Assert.ThrowsAsync<LoyaltyOperationException>(() =>
            fixture.Service.ActivateAsync(program.Id, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task Invalid_point_configuration_is_rejected()
    {
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<LoyaltyOperationException>(() =>
            fixture.Service.CreateAsync(new("Loyalty", null, 0, -1,
                Now, Now.AddDays(1), null, [fixture.RetailerId]), fixture.ActorId, CancellationToken.None));

        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task New_program_rejects_yesterday_and_today_starts_now()
    {
        var f = new Fixture();
        var data = new CreateLoyaltyProgramData("Loyalty", null, 100000, 0, Now, Now.AddDays(30), null, [f.RetailerId]);
        await Assert.ThrowsAsync<LoyaltyOperationException>(() => f.Service.CreateAsync(data with { StartAt = Now.AddDays(-1) }, f.ActorId, CancellationToken.None));
        var result = await f.Service.CreateAsync(data with { StartAt = Now.AddHours(-1) }, f.ActorId, CancellationToken.None);
        Assert.Equal(Now, result.StartAt);
    }

    [Fact]
    public async Task Rate_update_preserves_historical_start_and_cannot_rewrite_it()
    {
        var f = new Fixture();
        var p = f.Store.AddProgram(Now.AddDays(-5), Now.AddDays(30));
        p.IsActive = true;
        var data = new UpdateLoyaltyProgramData("Loyalty", null, 50000, 0, p.StartAt, p.EndAt, null, [f.RetailerId]);
        await f.Service.UpdateAsync(p.Id, data, CancellationToken.None);
        Assert.Equal(Now.AddDays(-5), p.StartAt);
        Assert.Equal(50000, p.SpendAmountPerPoint);
        await Assert.ThrowsAsync<LoyaltyOperationException>(() => f.Service.UpdateAsync(p.Id, data with { StartAt = Now }, CancellationToken.None));
        Assert.Equal(Now.AddDays(-5), p.StartAt);
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public Guid RetailerId => Store.RetailerId;
        public LoyaltyService Service { get; } = null!;

        public Fixture() => Service = new LoyaltyService(Store, new FixedTimeProvider(Now));
    }

    private sealed class FakeStore : ILoyaltyStore
    {
        public List<LoyaltyProgram> Programs { get; } = [];
        public Guid RetailerId { get; } = Guid.NewGuid();
        public List<LoyaltyProgramRetailer> ProgramRetailers { get; } = [];
        public Func<Guid, CancellationToken, Task<IReadOnlyCollection<Guid>>>? ReadProgramRetailers { get; set; }

        public Task<IReadOnlyCollection<LoyaltyProgram>> ListProgramsAsync(
            bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<LoyaltyProgram>>(
                Programs.Where(item => includeInactive || item.IsActive).ToArray());

        public Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Programs.SingleOrDefault(item => item.Id == id));

        public Task<string> NextProgramCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"LP-{Programs.Count + 1:000}");

        public Task<IReadOnlyCollection<Guid>> ListProgramRetailerIdsAsync(Guid programId, CancellationToken cancellationToken) =>
            ReadProgramRetailers?.Invoke(programId, cancellationToken) ??
            Task.FromResult<IReadOnlyCollection<Guid>>(ProgramRetailers.Where(item => item.LoyaltyProgramId == programId).Select(item => item.RetailerId).ToArray());

        public Task<IReadOnlyCollection<Guid>> FindExistingRetailerIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(ids.Contains(RetailerId) ? [RetailerId] : []);

        public void AddProgram(LoyaltyProgram program) => Programs.Add(program);

        public void AddProgramRetailers(IEnumerable<LoyaltyProgramRetailer> links) => ProgramRetailers.AddRange(links);

        public void RemoveProgramRetailers(IEnumerable<Guid> retailerIds, Guid programId) =>
            ProgramRetailers.RemoveAll(item => item.LoyaltyProgramId == programId && retailerIds.Contains(item.RetailerId));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public LoyaltyProgram AddProgram(DateTimeOffset startAt, DateTimeOffset endAt)
        {
            var program = new LoyaltyProgram
            {
                Id = Guid.NewGuid(), Code = $"LP-{Programs.Count + 1:000}", Name = "Test",
                SpendAmountPerPoint = 100_000m, StartAt = startAt, EndAt = endAt,
                CreatedByUserId = Guid.NewGuid(), CreatedAt = Now, UpdatedAt = Now
            };
            Programs.Add(program);
            return program;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
