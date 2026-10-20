using System.Security.Claims;
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
    [HttpGet]
    [ProducesResponseType<PagedUserResult>(StatusCodes.Status200OK)]
    public Task<PagedUserResult> List(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        identityService.ListUsersAsync(
            new UserListQuery(search, role, isActive, page, pageSize),
            cancellationToken);

    [HttpGet("{userId:guid}")]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    public Task<AuthenticatedUser> Get(Guid userId, CancellationToken cancellationToken) =>
        identityService.GetCurrentUserAsync(userId, cancellationToken);

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
            request.IdentityProvider,
            GetActorUserId(),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { userId = result.UserId }, result);
    }

    [HttpPut("{userId:guid}")]
    [HttpPatch("{userId:guid}")]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    public Task<AuthenticatedUser> UpdateProfile(
        Guid userId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken) =>
        identityService.UpdateProfileAsync(
            userId,
            request.Email,
            request.FullName,
            GetActorUserId(),
            cancellationToken);

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
        await identityService.UpdateRoleAsync(userId, request.Role, GetActorUserId(), cancellationToken);
        return NoContent();
    }

    [HttpPatch("{userId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateStatus(
        Guid userId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        await identityService.SetActiveStatusAsync(
            userId,
            request.IsActive,
            GetActorUserId(),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("{userId:guid}/scope/warehouses/{warehouseId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignWarehouse(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        await identityService.AssignWarehouseAsync(
            userId,
            warehouseId,
            GetActorUserId(),
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/scope/warehouses/{warehouseId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnassignWarehouse(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        await identityService.UnassignWarehouseAsync(
            userId,
            warehouseId,
            GetActorUserId(),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("{userId:guid}/scope/sales-groups/{salesGroupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignSalesGroup(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken)
    {
        await identityService.AssignSalesGroupAsync(
            userId,
            salesGroupId,
            GetActorUserId(),
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/scope/sales-groups/{salesGroupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnassignSalesGroup(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken)
    {
        await identityService.UnassignSalesGroupAsync(
            userId,
            salesGroupId,
            GetActorUserId(),
            cancellationToken);
        return NoContent();
    }

    [HttpPut("{userId:guid}/scope/regions/{regionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRegion(
        Guid userId,
        Guid regionId,
        CancellationToken cancellationToken)
    {
        await identityService.AssignRegionAsync(userId, regionId, GetActorUserId(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/scope/regions/{regionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnassignRegion(
        Guid userId,
        Guid regionId,
        CancellationToken cancellationToken)
    {
        await identityService.UnassignRegionAsync(userId, regionId, GetActorUserId(), cancellationToken);
        return NoContent();
    }

    [HttpPut("{userId:guid}/scope/retailers/{retailerId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRetailer(
        Guid userId,
        Guid retailerId,
        CancellationToken cancellationToken)
    {
        await identityService.AssignRetailerAsync(userId, retailerId, GetActorUserId(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/scope/retailers/{retailerId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnassignRetailer(
        Guid userId,
        Guid retailerId,
        CancellationToken cancellationToken)
    {
        await identityService.UnassignRetailerAsync(userId, retailerId, GetActorUserId(), cancellationToken);
        return NoContent();
    }

    private Guid GetActorUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId)
            ? userId
            : throw IdentityOperationException.InvalidToken();
    }
}
