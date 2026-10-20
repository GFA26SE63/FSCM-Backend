using FSCM.API.Contracts.Maps;
using FSCM.Application.Features.Maps;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Admin)]
[Route("api/v1/map")]
public sealed class MapController(MapService service) : ControllerBase
{
    [HttpPost("geocode")]
    public Task<MapLocationResult> Geocode(GeocodeRequest request, CancellationToken cancellationToken) =>
        service.GeocodeAsync(new GeocodeTarget(request.EntityType, request.EntityId, request.Address), cancellationToken);

    [HttpGet("locations/{entityType}/{entityId:guid}")]
    public Task<MapLocationResult> GetLocation(string entityType, Guid entityId, CancellationToken cancellationToken) =>
        service.GetLocationAsync(entityType, entityId, cancellationToken);

    [HttpPost("distances/refresh")]
    public Task<MapDistanceResult> RefreshDistance(RefreshDistanceRequest request, CancellationToken cancellationToken) =>
        service.RefreshDistanceAsync(request.WarehouseId, request.RetailerId, cancellationToken);

    [HttpGet("distances")]
    public Task<IReadOnlyCollection<MapDistanceResult>> ListDistances(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? retailerId, CancellationToken cancellationToken) =>
        service.ListDistancesAsync(warehouseId, retailerId, cancellationToken);
}
