using System.Security.Claims;
using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/roles")]
public sealed class RolesController(RoleService roleService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleSummaryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<RoleSummaryDto>> List(
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        roleService.ListRolesAsync(search, cancellationToken);

    [HttpGet("screens")]
    [ProducesResponseType<IReadOnlyList<ScreenFeatureDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ScreenFeatureDto>> GetScreens() =>
        Ok(roleService.GetScreenFeatures());

    [HttpGet("{roleId:guid}")]
    [ProducesResponseType<RoleDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<RoleDetailsDto> Get(Guid roleId, CancellationToken cancellationToken) =>
        roleService.GetRoleByIdAsync(roleId, cancellationToken);

    [HttpPost]
    [ProducesResponseType<RoleDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await roleService.CreateRoleAsync(request, GetActorUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { roleId = result.Id }, result);
    }

    [HttpPut("{roleId:guid}")]
    [ProducesResponseType<RoleDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<RoleDetailsDto> Update(
        Guid roleId,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken) =>
        roleService.UpdateRoleAsync(roleId, request, GetActorUserId(), cancellationToken);

    [HttpDelete("{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid roleId, CancellationToken cancellationToken)
    {
        await roleService.DeleteRoleAsync(roleId, GetActorUserId(), cancellationToken);
        return NoContent();
    }

    private Guid GetActorUserId()
    {
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawId, out var id) ? id : Guid.Empty;
    }
}
