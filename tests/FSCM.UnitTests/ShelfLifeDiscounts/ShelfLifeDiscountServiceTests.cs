using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.ShelfLifeDiscounts;

public sealed class ShelfLifeDiscountServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Product = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Category = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task A_sku_tier_beats_a_category_tier_covering_the_same_remaining_life()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Category, 0m, 50m, ShelfLifeDiscountTypes.Percent, 10m),
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, ShelfLifeDiscountTypes.Percent, 25m));

        var match = await fixture.Service.MatchAsync(Product, Category, 30m, 100_000m, null, CancellationToken.None);

        Assert.Equal(ShelfLifeDiscountScopes.Sku, match.Tier!.Scope);
        Assert.Equal(25_000m, match.DiscountAmountPerUnit);
    }

    [Fact]
    public async Task A_category_tier_applies_when_the_sku_has_none()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Category, 0m, 50m, ShelfLifeDiscountTypes.Percent, 10m));

        var match = await fixture.Service.MatchAsync(Product, Category, 30m, 100_000m, null, CancellationToken.None);

        Assert.Equal(ShelfLifeDiscountScopes.Category, match.Tier!.Scope);
        Assert.Equal(10_000m, match.DiscountAmountPerUnit);
    }

    [Fact]
    public async Task Nothing_matching_means_no_reduction_rather_than_an_error()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        var match = await fixture.Service.MatchAsync(Product, Category, 80m, 100_000m, null, CancellationToken.None);

        Assert.Null(match.Tier);
        Assert.Equal(0m, match.DiscountAmountPerUnit);
        Assert.Contains("bằng 0", match.Explanation);
    }

    [Fact]
    public async Task No_table_at_all_also_means_no_reduction_rather_than_an_error()
    {
        var fixture = new Fixture();

        // Nothing published. The answer is the same as a table with no band for this percentage:
        // the part keeps its base price. Refusing instead stopped the allocation of a deployment
        // that had simply not configured near-expiry pricing, and a run that cannot price cannot be
        // approved, reserved, picked or issued - one unpublished table held up every order.
        var match = await fixture.Service.MatchAsync(
            Product, Category, 80m, 100_000m, null, CancellationToken.None);

        Assert.Null(match.TableId);
        Assert.Equal(0, match.Version);
        Assert.Null(match.Tier);
        Assert.Equal(0m, match.DiscountAmountPerUnit);
        Assert.Contains("giá gốc", match.Explanation);
    }

    [Fact]
    public async Task A_moment_before_any_table_took_effect_also_prices_at_base()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        // Pricing a run from before the first table was published is a real case on any deployment
        // that configured discounts late, and the stored price has to stay readable afterwards.
        var match = await fixture.Service.MatchAsync(
            Product, Category, 10m, 100_000m, Now.AddDays(-30), CancellationToken.None);

        Assert.Null(match.TableId);
        Assert.Equal(0m, match.DiscountAmountPerUnit);
    }

    [Fact]
    public async Task The_upper_bound_of_a_band_is_exclusive()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 30m, ShelfLifeDiscountTypes.Percent, 20m),
            Tier(ShelfLifeDiscountScopes.Sku, 30m, 60m, ShelfLifeDiscountTypes.Percent, 10m));

        var below = await fixture.Service.MatchAsync(Product, Category, 29.99m, 100_000m, null, CancellationToken.None);
        var onBoundary = await fixture.Service.MatchAsync(Product, Category, 30m, 100_000m, null, CancellationToken.None);

        Assert.Equal(20_000m, below.DiscountAmountPerUnit);
        Assert.Equal(10_000m, onBoundary.DiscountAmountPerUnit);
    }

    [Fact]
    public async Task A_fixed_amount_larger_than_the_price_floors_at_zero()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 30m, ShelfLifeDiscountTypes.FixedPerUnit, 90_000m));

        var match = await fixture.Service.MatchAsync(Product, Category, 10m, 50_000m, null, CancellationToken.None);

        Assert.Equal(50_000m, match.DiscountAmountPerUnit);
    }

    [Fact]
    public async Task Matching_at_a_past_moment_uses_the_version_in_force_then()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, ShelfLifeDiscountTypes.Percent, 10m));
        await fixture.PublishAsync(
            effectiveFrom: Now.AddDays(7),
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, ShelfLifeDiscountTypes.Percent, 30m));

        var earlier = await fixture.Service.MatchAsync(
            Product, Category, 20m, 100_000m, Now.AddDays(1), CancellationToken.None);
        var current = await fixture.Service.MatchAsync(
            Product, Category, 20m, 100_000m, null, CancellationToken.None);

        Assert.Equal(1, earlier.Version);
        Assert.Equal(10_000m, earlier.DiscountAmountPerUnit);
        Assert.Equal(2, current.Version);
        Assert.Equal(30_000m, current.DiscountAmountPerUnit);
    }

    [Fact]
    public async Task Overlapping_bands_in_one_scope_are_refused()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() =>
            fixture.PublishAsync(
                Tier(ShelfLifeDiscountScopes.Sku, 0m, 40m, ShelfLifeDiscountTypes.Percent, 20m),
                Tier(ShelfLifeDiscountScopes.Sku, 30m, 60m, ShelfLifeDiscountTypes.Percent, 10m)));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Tables);
    }

    [Fact]
    public async Task A_sku_band_may_overlap_a_category_band_because_that_is_the_precedence_rule()
    {
        var fixture = new Fixture();

        var result = await fixture.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Category, 0m, 50m, ShelfLifeDiscountTypes.Percent, 10m),
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, ShelfLifeDiscountTypes.Percent, 25m));

        Assert.Equal(2, result.Tiers.Count);
    }

    [Theory]
    [InlineData(ShelfLifeDiscountTypes.Percent, 120d)]
    [InlineData(ShelfLifeDiscountTypes.Percent, -1d)]
    [InlineData(ShelfLifeDiscountTypes.FixedPerUnit, -5d)]
    public async Task An_out_of_range_discount_is_refused(string type, double value)
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() =>
            fixture.PublishAsync(Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, type, (decimal)value)));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Publishing_closes_the_previous_version()
    {
        var fixture = new Fixture();
        await fixture.PublishAsync(Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, ShelfLifeDiscountTypes.Percent, 10m));

        var second = await fixture.PublishAsync(
            effectiveFrom: Now.AddDays(3),
            Tier(ShelfLifeDiscountScopes.Sku, 0m, 50m, ShelfLifeDiscountTypes.Percent, 20m));

        Assert.Equal(2, second.Version);
        var first = fixture.Store.Tables.Single(item => item.Version == 1);
        Assert.False(first.IsActive);
        Assert.Equal(Now.AddDays(3), first.EffectiveTo);
    }

    [Fact]
    public async Task Scheduled_discount_does_not_change_current_price_before_effective_time()
    {
        var f = new Fixture();
        await f.PublishAsync(Tier(ShelfLifeDiscountScopes.Sku, 0, 50, ShelfLifeDiscountTypes.Percent, 10));
        await f.PublishAsync(Now.AddDays(7), Tier(ShelfLifeDiscountScopes.Sku, 0, 50, ShelfLifeDiscountTypes.Percent, 30));
        f.Clock.UtcNow = Now;
        Assert.Equal(10000m, (await f.Service.MatchAsync(Product, Category, 20, 100000, null, CancellationToken.None)).DiscountAmountPerUnit);
        f.Clock.UtcNow = Now.AddDays(7);
        Assert.Equal(30000m, (await f.Service.MatchAsync(Product, Category, 20, 100000, null, CancellationToken.None)).DiscountAmountPerUnit);
    }

    [Fact]
    public async Task Invalid_scope_and_date_do_not_close_previous_version()
    {
        var f = new Fixture();
        var tier = Tier(ShelfLifeDiscountScopes.Sku, 0, 50, ShelfLifeDiscountTypes.Percent, 10);
        await f.PublishAsync(tier);
        await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() => f.Service.PublishAsync(new(Now, null, [tier]), Actor, CancellationToken.None));
        await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() => f.Service.PublishAsync(new(Now.AddDays(1), null, [tier with { ProductCategoryId = Category }]), Actor, CancellationToken.None));
        f.Store.ScopeUsable = false;
        await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() => f.Service.PublishAsync(new(Now.AddDays(1), null, [tier]), Actor, CancellationToken.None));
        Assert.True(f.Store.Tables.Single().IsActive);
        Assert.Null(f.Store.Tables.Single().EffectiveTo);
    }

    [Fact]
    public async Task Excess_precision_cannot_round_into_overlapping_bands_in_PostgreSQL()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ShelfLifeDiscountOperationException>(() => f.PublishAsync(
            Tier(ShelfLifeDiscountScopes.Sku, 0, 20.001m, ShelfLifeDiscountTypes.Percent, 10)));
        Assert.Empty(f.Store.Tables);
    }

    private static ShelfLifeDiscountTierData Tier(
        string scope, decimal min, decimal max, string type, decimal value) =>
        new(scope,
            scope == ShelfLifeDiscountScopes.Sku ? Product : null,
            scope == ShelfLifeDiscountScopes.Category ? Category : null,
            min, max, type, value);

    private sealed class Fixture
    {
        public FakeStore Store { get; } = new();
        public ShelfLifeDiscountService Service { get; }

        public FixedClock Clock { get; } = new();
        public Fixture() => Service = new ShelfLifeDiscountService(Store, Clock);

        public Task<ShelfLifeDiscountTableResult> PublishAsync(params ShelfLifeDiscountTierData[] tiers) =>
            PublishAsync(Now, tiers);

        public async Task<ShelfLifeDiscountTableResult> PublishAsync(
            DateTimeOffset effectiveFrom, params ShelfLifeDiscountTierData[] tiers)
        {
            var result = await Service.PublishAsync(
                new PublishShelfLifeDiscountTableData(effectiveFrom, "Bậc giảm theo HSD", tiers),
                Actor, CancellationToken.None);
            Clock.UtcNow = effectiveFrom;
            return result;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now.AddDays(-1);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeStore : IShelfLifeDiscountStore
    {
        public bool ScopeUsable { get; set; } = true;
        public Task<bool> ScopeIsUsableAsync(string scope, Guid id, CancellationToken cancellationToken) => Task.FromResult(ScopeUsable);
        public List<ShelfLifeDiscountTable> Tables { get; } = [];
        public List<ShelfLifeDiscountTier> Tiers { get; } = [];

        public Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Tables.SingleOrDefault(item => item.IsActive)));

        public Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Tables.SingleOrDefault(item => item.Version == version)));

        public Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(
            DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Tables.SingleOrDefault(item =>
                item.EffectiveFrom <= at && (item.EffectiveTo is null || item.EffectiveTo > at))));

        public Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ShelfLifeDiscountTableView>>(
                Tables.Select(item => ToView(item)!).ToArray());

        public Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Tables.SingleOrDefault(item => item.IsActive));

        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Tables.Count == 0 ? 0 : Tables.Max(item => item.Version));

        public void AddTable(ShelfLifeDiscountTable table) => Tables.Add(table);

        public void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers) => Tiers.AddRange(tiers);

        public void AddAuditLog(AuditLog auditLog) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ShelfLifeDiscountTableView? ToView(ShelfLifeDiscountTable? table) => table is null
            ? null
            : new ShelfLifeDiscountTableView(table, Tiers
                .Where(item => item.ShelfLifeDiscountTableId == table.Id)
                .OrderBy(item => item.MinRemainingPercent)
                .ToArray());
    }
}
