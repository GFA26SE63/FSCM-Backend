using System.Security.Claims;
using FSCM.API.Contracts.Deliveries;
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

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("drivers")]
    public async Task<ActionResult<DriverResult>> CreateDriver(
        [FromBody] CreateDriverRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateDriverAsync(
            new CreateDriverData(request.WarehouseId, request.Code, request.FullName,
                request.Phone, request.LicenseNumber),
            GetActorRole(), cancellationToken);
        return CreatedAtAction(nameof(Drivers), new { }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPut("drivers/{id:guid}")]
    public Task<DriverResult> UpdateDriver(
        Guid id, [FromBody] UpdateDriverRequest request, CancellationToken cancellationToken) =>
        service.UpdateDriverAsync(id,
            new UpdateDriverData(request.FullName, request.Phone, request.LicenseNumber),
            GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPatch("drivers/{id:guid}/status")]
    public Task<DriverResult> SetDriverStatus(
        Guid id, [FromBody] SetActiveRequest request, CancellationToken cancellationToken) =>
        service.SetDriverActiveAsync(id, request.IsActive, GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("vehicles")]
    public async Task<ActionResult<VehicleResult>> CreateVehicle(
        [FromBody] CreateVehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateVehicleAsync(
            new CreateVehicleData(request.WarehouseId, request.PlateNumber,
                request.VehicleType, request.Capacity),
            GetActorRole(), cancellationToken);
        return CreatedAtAction(nameof(Vehicles), new { }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPut("vehicles/{id:guid}")]
    public Task<VehicleResult> UpdateVehicle(
        Guid id, [FromBody] UpdateVehicleRequest request, CancellationToken cancellationToken) =>
        service.UpdateVehicleAsync(id,
            new UpdateVehicleData(request.VehicleType, request.Capacity),
            GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPatch("vehicles/{id:guid}/status")]
    public Task<VehicleResult> SetVehicleStatus(
        Guid id, [FromBody] SetActiveRequest request, CancellationToken cancellationToken) =>
        service.SetVehicleActiveAsync(id, request.IsActive, GetActorRole(), cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw DeliveryOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw DeliveryOperationException.Validation("Authenticated user role is invalid.");
}
