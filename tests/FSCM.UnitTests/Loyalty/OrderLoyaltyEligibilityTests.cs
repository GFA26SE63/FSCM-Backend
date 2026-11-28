using System.Text.Json;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Loyalty;

public sealed class OrderLoyaltyEligibilityTests
{
    private static readonly DateTimeOffset ReceivedAt = DateTimeOffset.Parse("2026-11-29T10:00:00+07:00");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string Audit(OrderLoyaltyEligibility snapshot) => JsonSerializer.Serialize(new { LoyaltyEligibility = snapshot }, JsonOptions);

    [Fact]
    public async Task Order_received_before_expiry_earns_after_expiry_using_frozen_candidates_and_rates()
    {
        var retailer = Guid.NewGuid();
        var a = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-001", SpendAmountPerPoint = 100, IsActive = true,
            StartAt = ReceivedAt.AddDays(-5), EndAt = DateTimeOffset.Parse("2026-11-30T23:59:59+07:00") };
        var b = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-002", SpendAmountPerPoint = 50, IsActive = true,
            StartAt = a.StartAt, EndAt = a.EndAt };
        var json = Audit(OrderLoyaltyEligibility.Capture(retailer, ReceivedAt, [a, b]));
        // Configuration and audience may change after receipt; neither is read again for this order.
        a.SpendAmountPerPoint = 1; b.SpendAmountPerPoint = 999; b.IsActive = false;
        var frozen = OrderLoyaltyEligibility.Read(json, retailer, ReceivedAt)!;
        var selected = frozen.BestForAmount(1_000)!;
        Assert.Equal(b.Id, selected.Id); Assert.Equal(50, selected.SpendAmountPerPoint);
        var order = Guid.NewGuid(); var balance = Guid.NewGuid(); var store = new FakeLoyaltyAccrualStore();
        store.Views[order] = new(order, retailer, OrderStatuses.Completed, 1_000, true, false,
            new(selected.Id, selected.SpendAmountPerPoint), new(balance, 0, 0), false, false);
        var service = new LoyaltyAccrualService(store, new FixedTime(DateTimeOffset.Parse("2026-12-01T10:00:00+07:00")));
        Assert.Equal(20, (await service.EvaluateAsync(order, null, default)).Points);
        Assert.False((await service.EvaluateAsync(order, null, default)).Earned);
        Assert.Single(store.Rows, r => r.Type == LoyaltyTransactionTypes.Earned);
    }

    [Fact]
    public void Captured_empty_list_is_not_legacy_and_never_adopts_a_later_program()
    {
        var retailer = Guid.NewGuid();
        var frozen = OrderLoyaltyEligibility.Read(Audit(OrderLoyaltyEligibility.Capture(retailer, ReceivedAt, [])), retailer, ReceivedAt);
        Assert.NotNull(frozen); Assert.Empty(frozen.Programs); Assert.Null(frozen.BestForAmount(1_000));
        Assert.Null(OrderLoyaltyEligibility.Read("{}", retailer, ReceivedAt));
        Assert.Null(OrderLoyaltyEligibility.Read(null, retailer, ReceivedAt));
    }

    [Fact]
    public void Ties_use_captured_codes_and_malformed_snapshots_do_not_fall_back()
    {
        var retailer = Guid.NewGuid();
        var a = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-001", SpendAmountPerPoint = 70 };
        var b = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-002", SpendAmountPerPoint = 60 };
        var frozen = OrderLoyaltyEligibility.Capture(retailer, ReceivedAt, [b, a]);
        Assert.Equal(a.Id, frozen.BestForAmount(100)!.Id);
        foreach (var json in new[] { "{\"loyaltyEligibility\":null}", Audit(frozen with { Version = 2 }),
            Audit(frozen with { Programs = [new(Guid.NewGuid(), "LP-003", 0)] }) })
            Assert.Throws<LoyaltyOperationException>(() => OrderLoyaltyEligibility.Read(json, retailer, ReceivedAt));
        Assert.Throws<LoyaltyOperationException>(() => OrderLoyaltyEligibility.Read(Audit(frozen), Guid.NewGuid(), ReceivedAt));
    }

    private sealed class FixedTime(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
