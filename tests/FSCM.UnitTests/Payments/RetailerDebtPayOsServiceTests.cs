using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class RetailerDebtPayOsServiceTests
{
    [Fact]
    public async Task Checkout_replay_revalidates_the_linked_retailer_before_returning_the_payment_link()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var request = new CreateRetailerDebtPayOsCheckoutData(RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]);
        await service.CreateAsync(request, "private-key", fixture.UserId, SystemRoles.Retailer, default);
        var error = await Assert.ThrowsAsync<PaymentOperationException>(() => service.CreateAsync(
            request, "private-key", Guid.NewGuid(), SystemRoles.Retailer, default));
        Assert.Equal("forbidden", error.Code);
        Assert.Equal(1, fixture.Gateway.CreateCount);
    }

    [Fact]
    public async Task Selected_checkout_snapshots_server_collectible_and_replays_idempotently()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var request = new CreateRetailerDebtPayOsCheckoutData(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]);

        var first = await service.CreateAsync(request, "same-key", fixture.UserId,
            SystemRoles.Retailer, CancellationToken.None);
        var replay = await service.CreateAsync(request, "same-key", fixture.UserId,
            SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(800_000m, first.Amount);
        Assert.Equal(first.Id, replay.Id);
        Assert.Single(first.Allocations);
        Assert.Equal(800_000m, first.Allocations.Single().Amount);
        Assert.Equal(1, fixture.Gateway.CreateCount);
        Assert.Equal("CN DH-000101", fixture.Gateway.LastCreateData!.Description);
    }

    [Fact]
    public async Task Retailer_can_settle_an_outstanding_credit_balance_before_delivery()
    {
        var fixture = new Fixture();
        fixture.Payment.DeliveredDate = null;

        var checkout = await fixture.CreateService().CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "early-settlement",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(800_000m, checkout.Amount);
        Assert.Single(checkout.Allocations);
    }

    [Fact]
    public async Task Duplicate_success_webhook_creates_one_receipt_and_applies_snapshot_once()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var checkout = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "checkout-key",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        var payload = new PayOsWebhookPayload(
            checkout.PayOsOrderCode, 800_000, "FSCM", "1", "PAYOS-REF", "2026-11-11 09:00:00",
            "VND", checkout.PaymentLinkId, "00", "success", null, null, null, null, null, null);
        var verified = new PayOsVerifiedWebhook("00", true, payload);

        var first = await service.HandleVerifiedWebhookAsync(verified, CancellationToken.None);
        var replay = await service.HandleVerifiedWebhookAsync(verified, CancellationToken.None);

        Assert.True(first.Processed);
        Assert.True(replay.Processed);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(1_000_000m, fixture.Payment.ConfirmedAmount);
        Assert.Equal(PaymentStatuses.Paid, fixture.Payment.Status);
        Assert.Equal(first.PaymentId, replay.PaymentId);
    }

    [Fact]
    public async Task Report5_NFR02_selected_debt_never_overpays_and_duplicate_callback_allocates_once()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var checkout = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "nfr-02",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(800_000m, checkout.Amount);
        Assert.Equal(200_000m, fixture.Payment.ConfirmedAmount);
        Assert.Equal(PaymentStatuses.PartiallyPaid, fixture.Payment.Status);

        var payload = new PayOsWebhookPayload(
            checkout.PayOsOrderCode, 800_000, "FSCM", "1", "PAYOS-NFR-02", "2026-11-11 09:00:00",
            "VND", checkout.PaymentLinkId, "00", "success", null, null, null, null, null, null);
        var verified = new PayOsVerifiedWebhook("00", true, payload);
        await service.HandleVerifiedWebhookAsync(verified, CancellationToken.None);
        await service.HandleVerifiedWebhookAsync(verified, CancellationToken.None);

        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(fixture.Payment.Amount, fixture.Payment.ConfirmedAmount);
        Assert.Equal(PaymentStatuses.Paid, fixture.Payment.Status);
        Assert.True(fixture.Payment.ConfirmedAmount <= fixture.Payment.AdjustedAmount);
    }

    [Fact]
    public async Task Cancelled_payment_link_is_reconciled_and_releases_receivables()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var checkout = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "cancelled-checkout",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        fixture.Gateway.LinkStatus = "CANCELLED";

        var replacement = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "replacement-checkout",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(RetailerDebtPayOsStatuses.Cancelled, fixture.Store.Checkouts.Single(x => x.Id == checkout.Id).Status);
        Assert.NotEqual(checkout.Id, replacement.Id);
    }

    [Theory]
    [InlineData(RetailerDebtCheckoutModes.Selected)]
    [InlineData(RetailerDebtCheckoutModes.All)]
    public async Task Abandoned_pending_link_is_reopened_instead_of_blocking_a_new_checkout(string mode)
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var checkout = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "first-attempt",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        var retry = await service.CreateAsync(new(
            mode, mode == RetailerDebtCheckoutModes.All ? [] : [fixture.Payment.Id]), "second-attempt",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(checkout.Id, retry.Id);
        Assert.Equal(checkout.CheckoutUrl, retry.CheckoutUrl);
        Assert.Equal(1, fixture.Gateway.CreateCount);
        Assert.Single(fixture.Store.Checkouts);
    }

    [Theory]
    [InlineData(15, 900L)]
    [InlineData(0, null)]
    public async Task Configured_link_lifetime_sets_the_payos_deadline(int minutes, long? expectedSeconds)
    {
        var fixture = new Fixture();
        await fixture.CreateService(minutes).CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "lifetime",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(expectedSeconds + Fixture.Now.ToUnixTimeSeconds(), fixture.Gateway.LastCreateData!.ExpiredAt);
    }

    [Fact]
    public async Task Retailer_cancels_a_pending_checkout_and_can_pay_with_a_new_link()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var old = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "old",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        Assert.Equal(old.Id, Assert.Single(await service.ListPendingAsync(fixture.UserId, default)).Id);

        var cancelled = await service.CancelAsync(old.Id, fixture.UserId, default);
        fixture.Gateway.LinkStatus = "PENDING";
        var replacement = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.All, []), "new",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(RetailerDebtPayOsStatuses.Cancelled, cancelled.Status);
        Assert.Equal(1, fixture.Gateway.CancelCount);
        Assert.NotEqual(old.Id, replacement.Id);
        Assert.Equal(2, fixture.Gateway.CreateCount);
    }

    [Fact]
    public async Task Cancelling_a_link_paid_in_the_meantime_records_the_payment_instead()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var old = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "old",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        fixture.Gateway.PaidBeforeCancel = true;

        var result = await service.CancelAsync(old.Id, fixture.UserId, default);

        Assert.Equal(RetailerDebtPayOsStatuses.Paid, result.Status);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(PaymentStatuses.Paid, fixture.Payment.Status);
    }

    [Fact]
    public async Task Another_retailer_cannot_cancel_a_checkout()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var old = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "old",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        var error = await Assert.ThrowsAsync<PaymentOperationException>(
            () => service.CancelAsync(old.Id, Guid.NewGuid(), default));

        Assert.Equal("not_found", error.Code);
        Assert.Equal(0, fixture.Gateway.CancelCount);
    }

    [Fact]
    public async Task Pending_link_with_a_stale_balance_is_not_reopened()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "first-attempt",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        fixture.Payment.AdjustedAmount = 900_000m;

        var error = await Assert.ThrowsAsync<PaymentOperationException>(() => service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "second-attempt",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
        Assert.Equal(1, fixture.Gateway.CreateCount);
    }

    [Fact]
    public async Task Polling_a_paid_payment_link_confirms_the_debt_checkout_when_webhook_is_delayed()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var checkout = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "paid-polling",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        fixture.Gateway.LinkStatus = "PAID";

        var refreshed = await service.GetAsync(checkout.Id, fixture.UserId,
            SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(RetailerDebtPayOsStatuses.Paid, refreshed.Status);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(PaymentStatuses.Paid, fixture.Payment.Status);
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Retailer Retailer { get; }
        public Payment Payment { get; }
        public FakeStore Store { get; }
        public FakeGateway Gateway { get; } = new();

        public Fixture()
        {
            Retailer = new Retailer { Id = Guid.NewGuid(), UserAccountId = UserId, Name = "Cửa hàng An Bình" };
            var order = new SalesOrder
            {
                Id = Guid.NewGuid(), RetailerId = Retailer.Id, OrderCode = "DH-000101",
                RetailerNameSnapshot = Retailer.Name, RetailerCodeSnapshot = "10000001"
            };
            Payment = new Payment
            {
                Id = Guid.NewGuid(), SalesOrderId = order.Id, Method = OrderPaymentMethods.Credit,
                Status = PaymentStatuses.PartiallyPaid, Amount = 1_000_000m, AdjustedAmount = 1_000_000m,
                ConfirmedAmount = 200_000m, DeliveredDate = new DateOnly(2026, 11, 1), Version = 2
            };
            Store = new FakeStore(Retailer, new ReceiptPaymentView(Payment, order));
        }

        public static readonly DateTimeOffset Now = new(2026, 11, 11, 2, 0, 0, TimeSpan.Zero);

        public RetailerDebtPayOsService CreateService(int linkLifetimeMinutes = 0) => new(Store, Gateway,
            new FixedTimeProvider(Now), linkLifetime: new FixedLinkLifetime(linkLifetimeMinutes));
    }

    private sealed class FixedLinkLifetime(int minutes) : IPayOsLinkLifetimeSource
    {
        public Task<int> GetMinutesAsync(CancellationToken cancellationToken) => Task.FromResult(minutes);
    }

    private sealed class FakeGateway : IPayOsGateway
    {
        public int CreateCount { get; private set; }
        public PayOsCreateLinkData? LastCreateData { get; private set; }
        public string LinkStatus { get; set; } = "PENDING";
        public Task<PayOsLinkData> CreatePaymentLinkAsync(PayOsCreateLinkData data, CancellationToken cancellationToken)
        {
            CreateCount++;
            LastCreateData = data;
            return Task.FromResult(new PayOsLinkData(data.OrderCode, data.Amount, "link-1",
                "https://pay.payos.vn/web/link-1", "qr", "PENDING", 2_000_000_000));
        }
        public Task<PayOsVerifiedWebhook> VerifyWebhookAsync(PayOsWebhookEnvelope webhook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PayOsPaymentLinkStatus> GetPaymentLinkStatusAsync(long orderCode, CancellationToken cancellationToken) =>
            Task.FromResult(new PayOsPaymentLinkStatus(orderCode, "link-1", LinkStatus, 2_000_000_000));
        /// <summary>Simulates the retailer paying just before the cancel request reaches PayOS.</summary>
        public bool PaidBeforeCancel { get; set; }
        public int CancelCount { get; private set; }
        public Task<PayOsPaymentLinkStatus> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken)
        {
            CancelCount++;
            if (PaidBeforeCancel)
            {
                LinkStatus = "PAID";
                throw PaymentOperationException.Gateway("payOS could not cancel the payment link: paid");
            }
            LinkStatus = "CANCELLED";
            return Task.FromResult(new PayOsPaymentLinkStatus(orderCode, "link-1", LinkStatus, null));
        }
    }

    private sealed class FakeStore(Retailer retailer, ReceiptPaymentView payment) : IRetailerDebtPayOsStore
    {
        public List<RetailerDebtPayOsCheckout> Checkouts { get; } = [];
        public List<RetailerDebtPayOsAllocation> Allocations { get; } = [];
        public List<PaymentReceipt> Receipts { get; } = [];

        public Task<Retailer?> FindRetailerByUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<Retailer?>(retailer.UserAccountId == userId ? retailer : null);
        public Task<Retailer?> FindRetailerByIdAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult<Retailer?>(retailer.Id == retailerId ? retailer : null);
        public Task<IReadOnlyCollection<ReceiptPaymentView>> ListCollectibleForUpdateAsync(Guid retailerId, IReadOnlyCollection<Guid>? paymentIds, CancellationToken cancellationToken)
        {
            var available = Checkouts.Any(c => c.Status == RetailerDebtPayOsStatuses.Pending &&
                Allocations.Any(a => a.CheckoutId == c.Id && a.PaymentId == payment.Payment.Id))
                ? Array.Empty<ReceiptPaymentView>() : [payment];
            IReadOnlyCollection<ReceiptPaymentView> result = paymentIds is { Count: > 0 }
                ? available.Where(x => paymentIds.Contains(x.Payment.Id)).ToArray() : available;
            return Task.FromResult(result);
        }
        public Task<IReadOnlyCollection<RetailerDebtPayOsCheckout>> ListPendingForPaymentsAsync(
            Guid retailerId, IReadOnlyCollection<Guid>? paymentIds, CancellationToken cancellationToken)
        {
            var pending = Checkouts.Where(c => c.RetailerId == retailerId &&
                    c.Status == RetailerDebtPayOsStatuses.Pending &&
                    Allocations.Any(a => a.CheckoutId == c.Id &&
                        (paymentIds is null || paymentIds.Contains(a.PaymentId))))
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<RetailerDebtPayOsCheckout>>(pending);
        }
        public Task<RetailerDebtCheckoutView?> FindByOperationKeyAsync(string operationKey, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.OperationKey == operationKey)));
        public Task<RetailerDebtCheckoutView?> FindByPayOsOrderCodeAsync(long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.PayOsOrderCode == payOsOrderCode)));
        public Task<RetailerDebtCheckoutView?> FindByIdForRetailerAsync(Guid checkoutId, Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.Id == checkoutId && x.RetailerId == retailerId)));
        public Task<RetailerDebtCheckoutView?> FindByIdAsync(Guid checkoutId, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.Id == checkoutId)));
        public void AddCheckout(RetailerDebtPayOsCheckout checkout) => Checkouts.Add(checkout);
        public void AddAllocations(IEnumerable<RetailerDebtPayOsAllocation> allocations) => Allocations.AddRange(allocations);
        public void AddReceipt(PaymentReceipt receipt, IEnumerable<PaymentReceiptAllocation> allocations) => Receipts.Add(receipt);
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }
        private RetailerDebtCheckoutView? View(RetailerDebtPayOsCheckout? checkout) => checkout is null ? null :
            new(checkout, retailer, Allocations.Where(x => x.CheckoutId == checkout.Id).ToArray(), [payment]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
