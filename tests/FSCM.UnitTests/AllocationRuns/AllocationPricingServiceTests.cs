using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationPricingServiceTests
{
    private static readonly DateTimeOffset PricedAt = new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Category = Guid.NewGuid();

    [Fact]
    public async Task A_part_near_expiry_is_reduced_by_the_matching_tier()
    {
        var fixture = await Fixture.CreateAsync(
            Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        // 144 h left of a 720 h life is 20% remaining, inside the 0-30 band.
        var pricing = await fixture.PriceAsync(Part(remainingHours: 144, basePrice: 100_000m, quantity: 10m));

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        Assert.Equal(20m, part.RemainingPercent);
        Assert.Equal(20_000m, part.ShelfLifeDiscountPerUnit);
        Assert.Equal(80_000m, part.FinalNetUnitPrice);
        Assert.Equal(800_000m, part.Amount);
    }

    [Fact]
    public async Task A_part_that_matches_no_tier_keeps_its_base_price()
    {
        var fixture = await Fixture.CreateAsync(Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        var pricing = await fixture.PriceAsync(Part(remainingHours: 600, basePrice: 100_000m, quantity: 4m));

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        Assert.Null(part.TierId);
        Assert.Equal(0m, part.ShelfLifeDiscountPerUnit);
        Assert.Equal(100_000m, part.FinalNetUnitPrice);
    }

    [Fact]
    public async Task Every_part_is_measured_from_the_one_shared_pricing_moment()
    {
        var fixture = await Fixture.CreateAsync(Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        // Two batches of one order with the same expiry must reach the same remaining percentage,
        // whatever their arrivals would have been.
        var pricing = await fixture.PriceAsync(
            Part(remainingHours: 144, basePrice: 100_000m, quantity: 3m),
            Part(remainingHours: 144, basePrice: 100_000m, quantity: 7m));

        var parts = Assert.Single(pricing.Orders).Parts;
        Assert.Equal(parts[0].RemainingPercent, parts[1].RemainingPercent);
        Assert.Equal(PricedAt, pricing.PricedAt);
    }

    [Fact]
    public async Task Tiers_do_not_stack_even_when_a_sku_and_a_category_band_both_cover_the_part()
    {
        var fixture = await Fixture.CreateAsync(
            Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 10m, ShelfLifeDiscountScopes.Category),
            Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 25m));

        var pricing = await fixture.PriceAsync(Part(remainingHours: 144, basePrice: 100_000m, quantity: 1m));

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        // 25% from the SKU band alone, not 35% from both.
        Assert.Equal(25_000m, part.ShelfLifeDiscountPerUnit);
    }

    [Fact]
    public async Task An_expired_batch_is_reported_at_zero_percent_rather_than_a_negative()
    {
        var fixture = await Fixture.CreateAsync(Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        var pricing = await fixture.PriceAsync(Part(remainingHours: -48, basePrice: 100_000m, quantity: 1m));

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        Assert.Equal(0m, part.RemainingPercent);
        Assert.Equal(20_000m, part.ShelfLifeDiscountPerUnit);
    }

    [Fact]
    public async Task A_fixed_reduction_never_takes_the_price_below_zero()
    {
        var fixture = await Fixture.CreateAsync(
            Tier(0m, 30m, ShelfLifeDiscountTypes.FixedPerUnit, 90_000m));

        var pricing = await fixture.PriceAsync(Part(remainingHours: 144, basePrice: 50_000m, quantity: 2m));

        var part = Assert.Single(Assert.Single(pricing.Orders).Parts);
        Assert.Equal(0m, part.FinalNetUnitPrice);
        Assert.Equal(0m, part.Amount);
    }

    [Fact]
    public async Task Order_totals_add_up_across_parts()
    {
        var fixture = await Fixture.CreateAsync(Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        var pricing = await fixture.PriceAsync(
            Part(remainingHours: 144, basePrice: 100_000m, quantity: 2m),
            Part(remainingHours: 600, basePrice: 50_000m, quantity: 4m));

        var order = Assert.Single(pricing.Orders);
        Assert.Equal(400_000m, order.BaseAmount);
        Assert.Equal(40_000m, order.ShelfLifeDiscountAmount);
        Assert.Equal(360_000m, order.FinalAmount);
    }

    [Fact]
    public async Task The_discount_table_version_used_is_reported_on_every_part()
    {
        var fixture = await Fixture.CreateAsync(Tier(0m, 30m, ShelfLifeDiscountTypes.Percent, 20m));

        var pricing = await fixture.PriceAsync(Part(remainingHours: 144, basePrice: 100_000m, quantity: 1m));

        Assert.Equal(1, Assert.Single(Assert.Single(pricing.Orders).Parts).DiscountTableVersion);
    }

    private static ShelfLifeDiscountTierData Tier(
        decimal min, decimal max, string type, decimal value,
        string scope = ShelfLifeDiscountScopes.Sku) =>
        new(scope,
            scope == ShelfLifeDiscountScopes.Sku ? Product : null,
            scope == ShelfLifeDiscountScopes.Category ? Category : null,
            min, max, type, value);

    private static PricingPart Part(int remainingHours, decimal basePrice, decimal quantity) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Product, Category, "SKU001",
            quantity, PricedAt.AddHours(remainingHours), 720, basePrice * 1.2m, basePrice);

    private sealed class Fixture
    {
        private readonly AllocationPricingService _service;

        private Fixture(AllocationPricingService service) => _service = service;

        public static async Task<Fixture> CreateAsync(params ShelfLifeDiscountTierData[] tiers)
        {
            var store = new FakeStore();
            var discounts = new ShelfLifeDiscountService(store, new FixedClock());
            await discounts.PublishAsync(
                new PublishShelfLifeDiscountTableData(PricedAt.AddDays(-30), "Bậc giảm HSD", tiers),
                Actor, CancellationToken.None);
            return new Fixture(new AllocationPricingService(discounts));
        }

        public Task<AllocationPricing> PriceAsync(params PricingPart[] parts) =>
            _service.PriceAsync(
                PricedAt,
                [new PricingOrder(Guid.NewGuid(), "DH-000001", parts)],
                CancellationToken.None);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => PricedAt;
    }

    private sealed class FakeStore : IShelfLifeDiscountStore
    {
        private readonly List<ShelfLifeDiscountTable> _tables = [];
        private readonly List<ShelfLifeDiscountTier> _tiers = [];

        public Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.IsActive)));

        public Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item => item.Version == version)));

        public Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(
            DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(_tables.SingleOrDefault(item =>
                item.EffectiveFrom <= at && (item.EffectiveTo is null || item.EffectiveTo > at))));

        public Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ShelfLifeDiscountTableView>>(
                _tables.Select(item => ToView(item)!).ToArray());

        public Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.SingleOrDefault(item => item.IsActive));

        public Task<int> MaxVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_tables.Count == 0 ? 0 : _tables.Max(item => item.Version));

        public void AddTable(ShelfLifeDiscountTable table) => _tables.Add(table);

        public void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers) => _tiers.AddRange(tiers);

        public void AddAuditLog(AuditLog auditLog) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ShelfLifeDiscountTableView? ToView(ShelfLifeDiscountTable? table) => table is null
            ? null
            : new ShelfLifeDiscountTableView(table, _tiers
                .Where(item => item.ShelfLifeDiscountTableId == table.Id)
                .ToArray());
    }
}
