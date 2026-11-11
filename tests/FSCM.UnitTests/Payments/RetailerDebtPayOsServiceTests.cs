using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class RetailerDebtPayOsServiceTests
{
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
    public async Task Cancelled_payment_link_is_reconciled_and_releases_receivables()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();
        var checkout = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "cancelled-checkout",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);
        fixture.Gateway.LinkStatus = "CANCELLED";

        var reconciled = await service.GetAsync(checkout.Id, fixture.UserId,
            SystemRoles.Retailer, CancellationToken.None);
        var replacement = await service.CreateAsync(new(
            RetailerDebtCheckoutModes.Selected, [fixture.Payment.Id]), "replacement-checkout",
            fixture.UserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(RetailerDebtPayOsStatuses.Cancelled, reconciled.Status);
        Assert.NotEqual(checkout.Id, replacement.Id);
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

        public RetailerDebtPayOsService CreateService() => new(Store, Gateway,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 11, 2, 0, 0, TimeSpan.Zero)));
    }

    private sealed class FakeGateway : IPayOsGateway
    {
        public int CreateCount { get; private set; }
        public string LinkStatus { get; set; } = "PENDING";
        public Task<PayOsLinkData> CreatePaymentLinkAsync(PayOsCreateLinkData data, CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult(new PayOsLinkData(data.OrderCode, data.Amount, "link-1",
                "https://pay.payos.vn/web/link-1", "qr", "PENDING", 2_000_000_000));
        }
        public Task<PayOsVerifiedWebhook> VerifyWebhookAsync(PayOsWebhookEnvelope webhook, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PayOsPaymentLinkStatus> GetPaymentLinkStatusAsync(long orderCode, CancellationToken cancellationToken) =>
            Task.FromResult(new PayOsPaymentLinkStatus(orderCode, "link-1", LinkStatus, 2_000_000_000));
    }

    private sealed class FakeStore(Retailer retailer, ReceiptPaymentView payment) : IRetailerDebtPayOsStore
    {
        public List<RetailerDebtPayOsCheckout> Checkouts { get; } = [];
        public List<RetailerDebtPayOsAllocation> Allocations { get; } = [];
        public List<PaymentReceipt> Receipts { get; } = [];

        public Task<Retailer?> FindRetailerByUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<Retailer?>(retailer.UserAccountId == userId ? retailer : null);
        public Task<IReadOnlyCollection<ReceiptPaymentView>> ListCollectibleForUpdateAsync(Guid retailerId, IReadOnlyCollection<Guid>? paymentIds, CancellationToken cancellationToken)
        {
            var available = Checkouts.Any(c => c.Status == RetailerDebtPayOsStatuses.Pending &&
                Allocations.Any(a => a.CheckoutId == c.Id && a.PaymentId == payment.Payment.Id))
                ? Array.Empty<ReceiptPaymentView>() : [payment];
            IReadOnlyCollection<ReceiptPaymentView> result = paymentIds is { Count: > 0 }
                ? available.Where(x => paymentIds.Contains(x.Payment.Id)).ToArray() : available;
            return Task.FromResult(result);
        }
        public Task<RetailerDebtCheckoutView?> FindByOperationKeyAsync(string operationKey, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.OperationKey == operationKey)));
        public Task<RetailerDebtCheckoutView?> FindByPayOsOrderCodeAsync(long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.PayOsOrderCode == payOsOrderCode)));
        public Task<RetailerDebtCheckoutView?> FindByIdForRetailerAsync(Guid checkoutId, Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(View(Checkouts.SingleOrDefault(x => x.Id == checkoutId && x.RetailerId == retailerId)));
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
