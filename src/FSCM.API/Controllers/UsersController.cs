using FSCM.API.Contracts.Identity;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Admin)]
[Route("api/v1/users")]
public sealed class UsersController(IdentityService identityService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreatedUserResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await identityService.CreateUserAsync(
            request.Email,
            request.FullName,
            request.Role,
            cancellationToken);

        return CreatedAtAction(nameof(GetScope), new { userId = result.UserId }, result);
    }

    [HttpGet("{userId:guid}/scope")]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    public Task<AuthenticatedUser> GetScope(Guid userId, CancellationToken cancellationToken) =>
        identityService.GetCurrentUserAsync(userId, cancellationToken);

    [HttpPatch("{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateRole(
        Guid userId,
        UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.UpdateRoleAsync(userId, request.Role, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{userId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateStatus(
        Guid userId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.SetActiveStatusAsync(userId, request.IsActive, cancellationToken);
        return NoContent();
    }

    [HttpPut("{userId:guid}/scope/warehouses/{warehouseId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignWarehouse(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        await identityService.AssignWarehouseAsync(userId, warehouseId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/scope/warehouses/{warehouseId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnassignWarehouse(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        await identityService.UnassignWarehouseAsync(userId, warehouseId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{userId:guid}/scope/sales-groups/{salesGroupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignSalesGroup(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken)
    {
        await identityService.AssignSalesGroupAsync(userId, salesGroupId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/scope/sales-groups/{salesGroupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnassignSalesGroup(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken)
    {
        await identityService.UnassignSalesGroupAsync(userId, salesGroupId, cancellationToken);
        return NoContent();
    }
}

