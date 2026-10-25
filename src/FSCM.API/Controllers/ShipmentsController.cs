using System.Security.Claims;
using FSCM.API.Contracts.Shipments;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
[Route("api/v1/shipments")]
public sealed class ShipmentsController(ShipmentService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<ShipmentResult>> List(
        [FromQuery] Guid? orderId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), orderId, warehouseId, status, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ShipmentResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    /// <summary>F60: retailer delivery view of one order — per-warehouse shipments + payment status.</summary>
    [HttpGet("orders/{orderId:guid}/delivery-view")]
    public Task<RetailerOrderDeliveryResult> OrderDeliveryView(Guid orderId, CancellationToken cancellationToken) =>
        service.GetRetailerOrderDeliveryAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("orders/{orderId:guid}/sync")]
    public Task<IReadOnlyCollection<ShipmentResult>> Sync(Guid orderId, CancellationToken cancellationToken) =>
        service.SyncOrderAsync(orderId, GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPatch("{id:guid}/exception")]
    public Task<ShipmentResult> FlagException(
        Guid id, [FromBody] FlagShipmentExceptionRequest request, CancellationToken cancellationToken) =>
        service.FlagExceptionAsync(id,
            new FlagShipmentExceptionData(request.Reason, request.ExpectedVersion),
            GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPatch("{id:guid}/assignment")]
    public Task<ShipmentResult> Assign(
        Guid id, [FromBody] AssignShipmentRequest request, CancellationToken cancellationToken) =>
        service.AssignAsync(id,
            new AssignShipmentData(request.DriverId, request.VehicleId, request.ExpectedVersion),
            GetActorRole(), cancellationToken);

    /// <summary>
    /// F61: confirms actual received and damaged quantities for one warehouse shipment.
    /// Missing quantity is calculated by the server and the order completes only after every
    /// shipment is received without an exception.
    /// </summary>
    [Authorize(Roles = SystemRoles.Retailer)]
    [HttpPost("{id:guid}/receipt")]
    public Task<ShipmentResult> ConfirmReceipt(
        Guid id,
        [FromBody] ConfirmShipmentReceiptRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.ConfirmReceiptAsync(id,
            new ConfirmShipmentReceiptData(
                request.QrCodes,
                request.Lines.Select(line => new ConfirmShipmentReceiptLineData(
                    line.OrderLineId, line.BatchId, line.ReceivedQuantity, line.DamagedQuantity)).ToArray(),
                request.ExpectedVersion,
                request.Note),
            operationKey, GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw ShipmentOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw ShipmentOperationException.Validation("Authenticated user role is invalid.");
}
