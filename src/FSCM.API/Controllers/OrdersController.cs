using System.Security.Claims;
using FSCM.API.Contracts.Deliveries;
using FSCM.API.Contracts.Orders;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController(
    OrderService service,
    DeliveryService deliveryService,
    AllocationTraceService allocationTraceService,
    ShipmentService? shipmentService = null) : ControllerBase
{
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet]
    public Task<PagedOrderResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? retailerId,
        [FromQuery] bool createdByMe = false,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default,
        [FromQuery] int? size = null) =>
        service.ListAsync(GetActorUserId(), GetActorRole(),
            new(status, retailerId, createdByMe, search, Math.Max(1, page), Math.Clamp(size ?? pageSize, 1, 100)), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id}")]
    public Task<OrderDetailResult> Get(string id, CancellationToken cancellationToken) =>
        Guid.TryParse(id, out var guid)
            ? service.GetAsync(guid, GetActorUserId(), GetActorRole(), cancellationToken)
            : service.GetByCodeAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id}/allocation-trace")]
    [Obsolete("Use GET /api/v1/allocation-runs/orders/{salesOrderId}/trace.")]
    public async Task<OrderTraceDetailResult> Trace(string id, CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        await service.GetAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);
        return await allocationTraceService.GetByOrderAsync(orderId, cancellationToken);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id}/timeline")]
    public async Task<OrderTimelineResult> Timeline(string id, CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        return await service.GetTimelineAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);
    }

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpPost]
    [ProducesResponseType<OrderDetailResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateAssistedOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(
            request.ClientOrderKey,
            request.RetailerId,
            request.PaymentMethod,
            request.RequestedDeliveryDate,
            request.DeliveryAddress,
            request.Note,
            request.Lines.Select(item => new CreateOrderLineData(item.ProductId, item.Quantity)).ToArray()),
            GetActorUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpPost("offline")]
    [HttpPost("sync")]
    [ProducesResponseType<OrderDetailResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitOffline(
        SubmitOfflineOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitOfflineAsync(new(
            request.ClientOrderKey,
            request.RetailerId,
            request.PaymentMethod,
            request.RequestedDeliveryDate,
            request.DeliveryAddress,
            request.Note,
            request.OfflineEstimatedAmount,
            request.Lines.Select(item => new CreateOrderLineData(item.ProductId, item.Quantity)).ToArray(),
            request.SnapshotVersion,
            request.ConfirmConditionChanges,
            request.ExpectedConditions?.Select(item => new OfflineExpectedConditionData(
                item.ProductId, item.ExpectedBaseUnitPrice, item.ExpectedMlorVersion,
                item.ExpectedMlorRatio)).ToArray()),
            GetActorUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id}/approve")]
    public async Task<OrderDetailResult> Approve(
        string id,
        OrderDecisionRequest? request,
        CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        return await service.ApproveAsync(orderId, request?.ExpectedVersion ?? 0, GetActorUserId(), GetActorRole(), cancellationToken);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id}/reject")]
    public async Task<OrderDetailResult> Reject(
        string id,
        RejectOrderRequest? request,
        CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        return await service.RejectAsync(orderId, request?.ExpectedVersion ?? 0, request?.Reason ?? "Từ chối bởi Operator",
            GetActorUserId(), GetActorRole(), cancellationToken);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Sales}")]
    [HttpPost("{id}/cancel")]
    public async Task<OrderDetailResult> Cancel(
        string id,
        CancelOrderRequest? request,
        CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        return await service.CancelAsync(orderId, request?.ExpectedVersion ?? 0, GetActorUserId(), GetActorRole(),
            request?.Reason, cancellationToken);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id}/dispatch")]
    public async Task<DeliveryResult> Dispatch(
        string id,
        DispatchOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? operationKey,
        CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        return await deliveryService.DispatchAsync(orderId, new(
            request.DriverId, request.VehicleId, request.ExpectedOrderVersion, request.Note),
            operationKey ?? Guid.NewGuid().ToString("N"), GetActorUserId(), GetActorRole(), cancellationToken);
    }

    [Authorize(Roles = SystemRoles.Retailer)]
    [Obsolete("Use POST /api/v1/shipments/{id}/receipt for per-shipment actual quantities.")]
    [HttpPost("{id}/confirm-receipt")]
    public async Task<IActionResult> ConfirmReceipt(
        string id,
        ConfirmRetailerReceiptRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? operationKey,
        CancellationToken cancellationToken)
    {
        var orderId = await ResolveOrderIdAsync(id, cancellationToken);
        var qrCodes = request.ResolveQrCodes();
        var key = operationKey ?? Guid.NewGuid().ToString("N");

        if (shipmentService is not null && await shipmentService.HasShipmentsForOrderAsync(orderId, cancellationToken))
        {
            await shipmentService.ConfirmOrderShipmentsReceiptAsync(
                orderId, qrCodes, request.Note, key, GetActorUserId(), GetActorRole(), cancellationToken);
            var updatedOrder = await service.GetAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);
            return Ok(new
            {
                order = updatedOrder,
                pointsEarned = 0,
                pointsPending = updatedOrder.PaymentMethod == OrderPaymentMethods.Credit,
                id = orderId,
                orderId = updatedOrder.Id,
                orderCode = updatedOrder.OrderCode,
                orderStatus = updatedOrder.Status,
                status = updatedOrder.Status
            });
        }

        var legacyResult = await deliveryService.ConfirmReceiptAsync(orderId, new(
            qrCodes, request.ExpectedOrderVersion ?? 0, request.Note),
            key, GetActorUserId(), GetActorRole(), cancellationToken);
        var order = await service.GetAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);
        return Ok(new
        {
            order,
            pointsEarned = 0,
            pointsPending = order.PaymentMethod == OrderPaymentMethods.Credit,
            id = legacyResult.Id,
            orderId = legacyResult.OrderId,
            orderCode = legacyResult.OrderCode,
            orderStatus = legacyResult.OrderStatus,
            status = legacyResult.Status,
            driverId = legacyResult.DriverId,
            driverName = legacyResult.DriverName,
            driverPhone = legacyResult.DriverPhone,
            vehiclePlate = legacyResult.PlateNumber,
            receipt = legacyResult.Receipt
        });
    }

    private async Task<Guid> ResolveOrderIdAsync(string id, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(id, out var guid)) return guid;
        var order = await service.GetByCodeAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);
        return order.Id;
    }

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw OrderOperationException.Validation("Authenticated user role is invalid.");
}
