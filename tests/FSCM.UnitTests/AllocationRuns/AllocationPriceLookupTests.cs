using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationPriceLookupTests
{
    private static readonly DateTimeOffset PricedAt = new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid Order = Guid.NewGuid();

    [Fact]
    public async Task A_line_split_across_two_batches_is_rolled_up_into_one_line()
    {
        var line = Line(1, "SKU001", ordered: 10m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [line],
            [Price(line, quantity: 6m, listPrice: 120_000m, basePrice: 100_000m, discountPerUnit: 20_000m),
             Price(line, quantity: 4m, listPrice: 120_000m, basePrice: 100_000m, discountPerUnit: 0m)]));

        var priced = await new AllocationPriceLookupService(store).GetByOrderAsync(Order, default);

        var only = Assert.Single(priced.Lines);
        Assert.Equal(2, only.Parts.Count);
        Assert.Equal(10m, only.AllocatedQuantity);
        Assert.Equal(1_000_000m, only.BaseAmount);
        Assert.Equal(120_000m, only.ShelfLifeDiscountAmount);
        Assert.Equal(880_000m, only.FinalAmount);
        // The catalogue price stays readable next to what was actually charged.
        Assert.All(only.Parts, part => Assert.Equal(120_000m, part.ListUnitPrice));
    }

    [Fact]
    public async Task Order_totals_are_the_sum_of_its_lines()
    {
        var first = Line(1, "SKU001", ordered: 2m);
        var second = Line(2, "SKU002", ordered: 5m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [first, second],
            [Price(first, quantity: 2m, listPrice: 60_000m, basePrice: 50_000m, discountPerUnit: 5_000m),
             Price(second, quantity: 5m, listPrice: 30_000m, basePrice: 20_000m, discountPerUnit: 0m)]));

        var priced = await new AllocationPriceLookupService(store).GetByOrderAsync(Order, default);

        Assert.Equal(200_000m, priced.BaseAmount);
        Assert.Equal(10_000m, priced.ShelfLifeDiscountAmount);
        Assert.Equal(190_000m, priced.FinalAmount);
    }

    [Fact]
    public async Task Lines_come_back_in_order_number_sequence_whatever_order_they_were_stored_in()
    {
        var third = Line(3, "SKU003", ordered: 1m);
        var first = Line(1, "SKU001", ordered: 1m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [third, first],
            [Price(third, 1m, 1m, 1m, 0m), Price(first, 1m, 1m, 1m, 0m)]));

        var priced = await new AllocationPriceLookupService(store).GetByOrderAsync(Order, default);

        Assert.Equal([1, 3], priced.Lines.Select(line => line.LineNumber));
    }

    [Fact]
    public async Task A_price_from_a_version_still_awaiting_approval_is_not_reported_as_approved()
    {
        var line = Line(1, "SKU001", ordered: 1m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.AwaitingApproval,
            [line],
            [Price(line, 1m, 120_000m, 100_000m, 0m)]));

        var priced = await new AllocationPriceLookupService(store).GetByOrderAsync(Order, default);

        // A quotation. Collection and complaints must be able to tell it from a settled price.
        Assert.False(priced.IsApproved);
        Assert.Equal(AllocationPlanVersionStatuses.AwaitingApproval, priced.PlanVersionStatus);
    }

    [Fact]
    public async Task An_approved_version_is_reported_as_approved()
    {
        var line = Line(1, "SKU001", ordered: 1m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [line],
            [Price(line, 1m, 120_000m, 100_000m, 0m)]));

        Assert.True((await new AllocationPriceLookupService(store).GetByOrderAsync(Order, default)).IsApproved);
    }

    [Fact]
    public async Task The_pricing_moment_and_its_inputs_travel_with_every_part()
    {
        var line = Line(1, "SKU001", ordered: 1m);
        var expiry = PricedAt.AddHours(144);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [line],
            [Price(line, 1m, 120_000m, 100_000m, 20_000m, expiresAt: expiry)]));

        var priced = await new AllocationPriceLookupService(store).GetByOrderAsync(Order, default);

        var part = Assert.Single(Assert.Single(priced.Lines).Parts);
        Assert.Equal(PricedAt, priced.PricedAt);
        Assert.Equal(PricedAt, part.PricedAt);
        // Enough to recheck the percentage rather than take it on trust.
        Assert.Equal(expiry, part.BatchExpiresAt);
        Assert.Equal(720, part.TotalShelfLifeHours);
        Assert.Equal(20m, part.RemainingShelfLifePercent);
    }

    [Fact]
    public async Task A_line_whose_priced_parts_do_not_cover_what_the_run_allocated_is_refused()
    {
        // The run recorded 10 allocated; only 6 of them were priced.
        var line = Line(1, "SKU001", ordered: 10m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [line],
            [Price(line, quantity: 6m, listPrice: 120_000m, basePrice: 100_000m, discountPerUnit: 0m)]));

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationPriceLookupService(store).GetByOrderAsync(Order, default));

        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task A_part_whose_amount_no_longer_matches_its_components_is_refused()
    {
        var line = Line(1, "SKU001", ordered: 2m);
        var drifted = Price(line, quantity: 2m, listPrice: 120_000m, basePrice: 100_000m, discountPerUnit: 0m);
        drifted.Amount = 999m;
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [line], [drifted]));

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationPriceLookupService(store).GetByOrderAsync(Order, default));

        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task A_part_whose_unit_price_does_not_match_its_reduction_is_refused()
    {
        var line = Line(1, "SKU001", ordered: 2m);
        var drifted = Price(line, quantity: 2m, listPrice: 120_000m, basePrice: 100_000m, discountPerUnit: 20_000m);
        drifted.FinalNetUnitPrice = 100_000m; // the reduction was recorded but never taken off
        drifted.Amount = 200_000m;
        var store = new FakeStore(View(AllocationPlanVersionStatuses.Approved, [line], [drifted]));

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationPriceLookupService(store).GetByOrderAsync(Order, default));

        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task An_order_no_run_ever_allocated_is_not_found()
    {
        var store = new FakeStore(null);

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationPriceLookupService(store).GetByOrderAsync(Order, default));

        Assert.Equal("not_found", error.Code);
    }

    [Fact]
    public async Task One_line_can_be_looked_up_on_its_own()
    {
        var first = Line(1, "SKU001", ordered: 2m);
        var second = Line(2, "SKU002", ordered: 5m);
        var store = new FakeStore(View(
            AllocationPlanVersionStatuses.Approved,
            [first, second],
            [Price(first, 2m, 60_000m, 50_000m, 0m), Price(second, 5m, 30_000m, 20_000m, 0m)]));

        var line = await new AllocationPriceLookupService(store)
            .GetByOrderLineAsync(second.SalesOrderLineId, default);

        Assert.Equal("SKU002", line.Sku);
        Assert.Equal(100_000m, line.FinalAmount);
    }

    [Fact]
    public async Task A_line_no_run_ever_allocated_is_not_found()
    {
        var store = new FakeStore(null);

        var error = await Assert.ThrowsAsync<AllocationRunOperationException>(
            () => new AllocationPriceLookupService(store).GetByOrderLineAsync(Guid.NewGuid(), default));

        Assert.Equal("not_found", error.Code);
    }

    /// <summary>A line the run fully allocated; `allocated` differs only where a test needs it to.</summary>
    private static AllocationRunLine Line(int lineNumber, string sku, decimal ordered, decimal? allocated = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            SalesOrderLineId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            SkuSnapshot = sku,
            LineNumber = lineNumber,
            OrderedQuantity = ordered,
            TotalShelfLifeHours = 720,
            AllocatedQuantity = allocated ?? ordered,
        };

    private static AllocationPlanPrice Price(
        AllocationRunLine line,
        decimal quantity,
        decimal listPrice,
        decimal basePrice,
        decimal discountPerUnit,
        DateTimeOffset? expiresAt = null)
    {
        var expiry = expiresAt ?? PricedAt.AddHours(600);
        var remaining = Math.Max(0m, Math.Round(
            (decimal)(expiry - PricedAt).TotalHours / 720m * 100m, 2, MidpointRounding.AwayFromZero));
        return new AllocationPlanPrice
        {
            Id = Guid.NewGuid(),
            AllocationRunLineId = line.Id,
            BatchId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Quantity = quantity,
            ListUnitPrice = listPrice,
            BaseNetUnitPrice = basePrice,
            PricedAt = PricedAt,
            BatchExpiresAt = expiry,
            TotalShelfLifeHours = 720,
            RemainingShelfLifePercent = remaining,
            ShelfLifeDiscountPerUnit = discountPerUnit,
            FinalNetUnitPrice = basePrice - discountPerUnit,
            Amount = (basePrice - discountPerUnit) * quantity,
            DiscountTableVersion = 1,
            Explanation = "test",
        };
    }

    private static OrderPriceView View(
        string status,
        IReadOnlyList<AllocationRunLine> lines,
        IReadOnlyList<AllocationPlanPrice> prices) =>
        new("DH-000001",
            Guid.NewGuid(),
            "PB-20261022-01",
            new AllocationPlanVersion
            {
                Id = Guid.NewGuid(),
                VersionNumber = 1,
                Status = status,
                PricedAt = PricedAt,
            },
            lines.Select(line => new OrderPriceLineView(line)).ToArray(),
            prices.Select(price => new OrderPricePartView(price, "LO-0001")).ToArray());

    private sealed class FakeStore(OrderPriceView? view) : StubAllocationRunStore
    {
        public override Task<OrderPriceView?> FindOrderPriceAsync(
            Guid salesOrderId, CancellationToken cancellationToken) => Task.FromResult(view);

        public override Task<Guid?> FindOrderIdForLineAsync(
            Guid salesOrderLineId, CancellationToken cancellationToken) =>
            Task.FromResult(view is not null && view.Lines.Any(line => line.Line.SalesOrderLineId == salesOrderLineId)
                ? Order
                : (Guid?)null);
    }
}
