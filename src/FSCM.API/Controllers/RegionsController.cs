using FSCM.API.Contracts.Territory;
using FSCM.Application.Features.Territory;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/regions")]
public sealed class RegionsController(TerritoryService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<RegionResult>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListRegionsAsync(includeInactive, cancellationToken);

    [HttpGet("{regionId:guid}")]
    public Task<RegionResult> Get(Guid regionId, CancellationToken cancellationToken) =>
        service.GetRegionAsync(regionId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateRegionRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateRegionAsync(request.Code, request.Name, request.Description, cancellationToken);
        return CreatedAtAction(nameof(Get), new { regionId = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{regionId:guid}")]
    public Task<RegionResult> Update(Guid regionId, UpdateRegionRequest request, CancellationToken cancellationToken) =>
        service.UpdateRegionAsync(regionId, request.Name, request.Description, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPatch("{regionId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid regionId, UpdateTerritoryStatusRequest request, CancellationToken cancellationToken)
    {
        await service.SetRegionStatusAsync(regionId, request.IsActive, cancellationToken);
        return NoContent();
    }
}
