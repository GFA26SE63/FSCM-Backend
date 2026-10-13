using System.Security.Cryptography;
using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Contracts;
using FSCM.Modules.Logistics.Contracts;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Ordering.Domain.Picking;
using FSCM.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Ordering.Application;

/// <summary>Order lifecycle after placement: approve / reject / cancel, dispatch with vehicle + driver, receipt confirmation by QR, completion when credit is paid.</summary>
public sealed class OrderWorkflowService(IOrderRepository orders, IPickingRepository pickings, IOrderingUnitOfWork uow, ILogisticsModule logistics, ICurrentUser user, IClock clock)
{
    public async Task ApproveAsync(string code, CancellationToken ct)
    {
        var o = await Load(code, ct); var now = clock.UtcNow;
        o.Approve(user.UserId, user.FullName, now);
        var idx = 0;
        foreach (var g in o.Lines.SelectMany(l => l.Allocations.Select(a => (Line: l, Alloc: a))).GroupBy(x => x.Alloc.WarehouseCode).OrderBy(g => g.Key, StringComparer.Ordinal))
            pickings.Add(new PickingList(o.Id, idx++, g.Key, g.Select(x => (x.Alloc.BatchCode, x.Line.Sku, x.Alloc.Quantity)), now));
        uow.Enqueue(new OrderApproved(o.Id, o.RetailerCode, o.SalesUserId));
        await uow.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(string code, string reason, CancellationToken ct)
    {
        var o = await Load(code, ct); o.Reject(user.UserId, user.FullName, reason, clock.UtcNow);
        uow.Enqueue(new OrderRejected(o.Id, o.RetailerCode, o.SalesUserId, reason, StockWasHeld: true));
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Retailer / Sales may cancel their own pending order; Operator / Admin may cancel until picking starts.</summary>
    public async Task CancelAsync(string code, CancellationToken ct)
    {
        var o = await Load(code, ct);
        Guard.Against(user.Role == Role.Retailer && (user.RetailerCode != o.RetailerCode || o.Status != OrderStatus.Pending), "order.cancel", "Retailer chỉ hủy được đơn của mình khi còn chờ duyệt.");
        Guard.Against(user.Role == Role.Sales && (o.SalesUserId != user.UserId || o.Status != OrderStatus.Pending), "order.cancel", "Sales chỉ hủy được đơn của mình khi còn chờ duyệt.");
        o.Cancel(user.UserId, user.FullName, clock.UtcNow);
        foreach (var p in await pickings.ByOrderAsync(o.Id, ct)) p.Cancel();
        uow.Enqueue(new OrderCancelled(o.Id, o.RetailerCode, o.SalesUserId, user.FullName, StockWasHeld: true));
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>"Đang giao": vehicle + driver come from master data (no driver account); a snapshot and the estimated delivery cost are stored on the order.</summary>
    public async Task DispatchAsync(string code, DispatchInput x, CancellationToken ct)
    {
        var o = await Load(code, ct);
        Guard.Against(o.Status != OrderStatus.Shipped, "order.transition", $"Đơn {o.Id} phải ở trạng thái Đã xuất kho mới giao được.");
        var a = await logistics.AssignDeliveryAsync(x.Plate, x.DriverCode, o.TotalCases, ct);
        decimal cost = 0;
        foreach (var wh in o.Warehouses) cost += (await logistics.QuoteAsync(wh, o.RetailerCode, Purpose.WarehouseToRetailer, clock.Today, ct))?.TripCost ?? 0;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        o.Dispatch(new DeliverySnapshot(a.Plate, a.VehicleType, a.DriverCode, a.DriverName, a.DriverPhone), cost, token, user.UserId, user.FullName, clock.UtcNow);
        uow.Enqueue(new OrderShipped(o.Id, o.RetailerCode, o.SalesUserId, a.Plate, a.DriverName, a.DriverPhone));
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Retailer scans the QR printed on the delivery note → Delivered (and Completed for COD / prepaid) → Finance and Loyalty react to OrderDelivered.</summary>
    public async Task ConfirmReceiptAsync(string code, string qrToken, CancellationToken ct)
    {
        var o = await Load(code, ct);
        Guard.Against(user.Role != Role.Retailer || user.RetailerCode != o.RetailerCode, "order.receipt", "Chỉ retailer của đơn mới được xác nhận nhận hàng.");
        o.ConfirmReceipt(qrToken, user.UserId, user.FullName, clock.UtcNow, clock.Today);
        uow.Enqueue(Delivered(o));
        if (o.Status == OrderStatus.Completed) uow.Enqueue(Completed(o));
        await uow.SaveChangesAsync(ct);
    }

    internal static OrderDelivered Delivered(Order o) => new(o.Id, o.RetailerCode, o.SalesUserId, o.Payment.ToString(), o.CreditTermDays, o.GrossAmount, o.DiscountAmount, o.NetAmount, o.DeliveredOn!.Value, Lines(o));
    internal static OrderCompleted Completed(Order o) => new(o.Id, o.RetailerCode, o.SalesUserId, o.Payment.ToString(), o.GrossAmount, o.DiscountAmount, o.NetAmount, o.CompletedOn!.Value, Lines(o));
    private static IReadOnlyList<OrderLineInfo> Lines(Order o) => o.Lines.Select(l => new OrderLineInfo(l.Sku, l.Quantity, l.GrossAmount, l.DiscountAmount, l.NetAmount)).ToList();

    private async Task<Order> Load(string code, CancellationToken ct) => await orders.GetAsync(code, ct) ?? throw new BusinessRuleException("order.not_found", $"Không có đơn {code}.");
}

/// <summary>Ordering ← CreditOrderPaid: a delivered credit order becomes Completed (this is what feeds KPI / ranking).</summary>
public sealed class CreditOrderPaidHandler(IOrderRepository orders, IOrderingUnitOfWork uow, IClock clock) : IIntegrationEventHandler<CreditOrderPaid>
{
    public async Task HandleAsync(CreditOrderPaid e, CancellationToken ct = default)
    {
        var o = await orders.GetAsync(e.OrderCode, ct);
        if (o is null || o.Status != OrderStatus.Delivered) return;                    // idempotent
        o.Complete("system", "Hệ thống", clock.UtcNow, e.PaidOn);
        uow.Enqueue(OrderWorkflowService.Completed(o));
        await uow.SaveChangesAsync(ct);
    }
}
