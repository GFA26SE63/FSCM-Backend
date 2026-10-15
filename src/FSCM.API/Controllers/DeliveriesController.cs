using System.Security.Claims;
using FSCM.Application.Features.Deliveries;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
[Route("api/v1/deliveries")]
public sealed class DeliveriesController(DeliveryService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<DeliveryResult>> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), status, cancellationToken);

    [HttpGet("orders/{orderId:guid}")]
    public Task<DeliveryResult> GetByOrder(Guid orderId, CancellationToken cancellationToken) =>
        service.GetByOrderAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpGet("drivers")]
    public Task<IReadOnlyCollection<DriverResult>> Drivers(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        service.ListDriversAsync(GetActorRole(), includeInactive, cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpGet("vehicles")]
    public Task<IReadOnlyCollection<VehicleResult>> Vehicles(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        service.ListVehiclesAsync(GetActorRole(), includeInactive, cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw DeliveryOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw DeliveryOperationException.Validation("Authenticated user role is invalid.");
}
