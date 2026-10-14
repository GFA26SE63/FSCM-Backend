using FSCM.API.Contracts.Territory;
using FSCM.Application.Features.Territory;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sales-groups")]
public sealed class SalesGroupsController(TerritoryService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<SalesGroupResult>> List(
        [FromQuery] Guid? regionId, [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        service.ListSalesGroupsAsync(regionId, includeInactive, cancellationToken);

    [HttpGet("{salesGroupId:guid}")]
    public Task<SalesGroupResult> Get(Guid salesGroupId, CancellationToken cancellationToken) =>
        service.GetSalesGroupAsync(salesGroupId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateSalesGroupRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateSalesGroupAsync(request.RegionId, request.Code, request.Name, cancellationToken);
        return CreatedAtAction(nameof(Get), new { salesGroupId = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{salesGroupId:guid}")]
    public Task<SalesGroupResult> Update(
        Guid salesGroupId, UpdateSalesGroupRequest request, CancellationToken cancellationToken) =>
        service.UpdateSalesGroupAsync(salesGroupId, request.Name, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPatch("{salesGroupId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid salesGroupId, UpdateTerritoryStatusRequest request, CancellationToken cancellationToken)
    {
        await service.SetSalesGroupStatusAsync(salesGroupId, request.IsActive, cancellationToken);
        return NoContent();
    }
}
