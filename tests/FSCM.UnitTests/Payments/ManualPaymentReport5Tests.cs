using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class ManualPaymentReport5Tests
{
    [Fact]
    public async Task UT_Manual_Payment_UTCID01_cod_cannot_pass_the_purchase_order_gate()
    {
        var store = new GateStore("COD", PaymentStatuses.Paid);
        var service = new PurchaseOrderGateService(store, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TryCreateAsync(store.Order.Id, default));

        Assert.Null(store.PurchaseOrder);
        Assert.Empty(store.PickingLists);
        Assert.Empty(store.History);
    }

    [Fact]
    public async Task UT_Manual_Payment_UTCID02_verified_prepaid_payment_creates_one_po_even_when_replayed()
    {
        var store = new GateStore(OrderPaymentMethods.Prepaid, PaymentStatuses.Unpaid);
        var service = new PurchaseOrderGateService(store, TimeProvider.System);

        await service.TryCreateAsync(store.Order.Id, default);
        Assert.Null(store.PurchaseOrder);

        store.Payment.Status = PaymentStatuses.Paid;
        await service.TryCreateAsync(store.Order.Id, default);
        await service.TryCreateAsync(store.Order.Id, default);

        var purchaseOrder = Assert.IsType<PurchaseOrder>(store.PurchaseOrder);
        Assert.Equal(OrderPaymentMethods.Prepaid, purchaseOrder.PaymentMethod);
        Assert.Equal(1, store.PurchaseOrderAddCount);
        Assert.Single(store.PickingLists);
        Assert.Single(store.History);
    }

    private sealed class GateStore : IPurchaseOrderGateStore
    {
        public SalesOrder Order { get; } = new() { Id = Guid.NewGuid(), OrderCode = "SO-MANUAL-PAY", Status = OrderStatuses.Quoted };
        public SalesOrderQuote Quote { get; } = new() { Id = Guid.NewGuid(), Status = SalesOrderQuoteStatuses.Accepted, RevisionNumber = 1, AcceptedByUserId = Guid.NewGuid() };
        public Payment Payment { get; } = new();
        public Allocation Allocation { get; } = new() { Id = Guid.NewGuid(), BatchId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(), AllocatedQuantity = 1m };
        public PurchaseOrder? PurchaseOrder { get; private set; }
        public int PurchaseOrderAddCount { get; private set; }
        public List<PickingList> PickingLists { get; } = [];
        public List<OrderStatusHistory> History { get; } = [];

        public GateStore(string paymentMethod, string paymentStatus)
        {
            Order.PaymentMethod = paymentMethod;
            Quote.PurchaseOrderId = null;
            Payment.Status = paymentStatus;
        }

        public Task<PurchaseOrderGateView?> LoadForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<PurchaseOrderGateView?>(new(Order, Quote, Payment, PurchaseOrder,
                [Allocation], new Dictionary<Guid, string> { [Allocation.WarehouseId] = "WH-MANUAL" }));
        public void AddPurchaseOrder(PurchaseOrder purchaseOrder) { PurchaseOrder = purchaseOrder; PurchaseOrderAddCount++; }
        public void AddPickingList(PickingList pickingList) => PickingLists.Add(pickingList);
        public void AddPickingItem(PickingItem pickingItem) { }
        public void AddHistory(OrderStatusHistory history) => History.Add(history);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }
    }
}
