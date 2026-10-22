using FSCM.API.Contracts.WarehouseConfiguration;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/warehouses")]
public sealed class WarehousesController(WarehouseConfigurationService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<WarehouseResult>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListWarehousesAsync(includeInactive, cancellationToken);

    [HttpGet("{warehouseId:guid}")]
    public Task<WarehouseResult> Get(Guid warehouseId, CancellationToken cancellationToken) =>
        service.GetWarehouseAsync(warehouseId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateWarehouseAsync(new SaveWarehouseData(
            request.Code, request.Name, request.Type, request.Address,
            request.Latitude, request.Longitude, request.ProviderPlaceId,
            request.MinimumStockThresholdCartons), cancellationToken);
        return CreatedAtAction(nameof(Get), new { warehouseId = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{warehouseId:guid}")]
    public Task<WarehouseResult> Update(
        Guid warehouseId, UpdateWarehouseRequest request, CancellationToken cancellationToken) =>
        service.UpdateWarehouseAsync(warehouseId, new SaveWarehouseData(
            string.Empty, request.Name, request.Type, request.Address,
            request.Latitude, request.Longitude, request.ProviderPlaceId,
            request.MinimumStockThresholdCartons), cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPatch("{warehouseId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid warehouseId, UpdateWarehouseStatusRequest request, CancellationToken cancellationToken)
    {
        await service.SetWarehouseStatusAsync(warehouseId, request.IsActive, cancellationToken);
        return NoContent();
    }

}
