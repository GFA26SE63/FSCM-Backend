using System.Security.Claims;
using FSCM.API.Contracts.Territory;
using FSCM.Application.Features.Territory;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.SalesManager}")]
[Route("api/v1/territory-assignments")]
public sealed class TerritoryAssignmentsController(TerritoryService service) : ControllerBase
{
    [HttpGet("sales")]
    public Task<IReadOnlyCollection<SalesGroupMembershipResult>> ListSales(
        [FromQuery] Guid? salesGroupId, [FromQuery] Guid? userId,
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListMembershipsAsync(salesGroupId, userId, includeInactive,
            GetActorUserId(), User.IsInRole(SystemRoles.Admin), cancellationToken);

    [HttpPut("sales/{userId:guid}")]
    public Task<SalesGroupMembershipResult> TransferSales(
        Guid userId, TransferSalesRequest request, CancellationToken cancellationToken) =>
        service.TransferSalesAsync(userId, request.DestinationSalesGroupId,
            GetActorUserId(), User.IsInRole(SystemRoles.Admin), cancellationToken);

    [HttpGet("retailers")]
    public Task<IReadOnlyCollection<RetailerTerritoryAssignmentResult>> ListRetailers(
        [FromQuery] Guid? retailerId, [FromQuery] Guid? salesGroupId,
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListRetailerAssignmentsAsync(retailerId, salesGroupId, includeInactive,
            GetActorUserId(), User.IsInRole(SystemRoles.Admin), cancellationToken);

    [HttpPut("retailers/{retailerId:guid}")]
    public Task<RetailerTerritoryAssignmentResult> AssignRetailer(
        Guid retailerId, AssignRetailerTerritoryRequest request, CancellationToken cancellationToken) =>
        service.AssignRetailerAsync(retailerId, request.DestinationSalesGroupId,
            GetActorUserId(), User.IsInRole(SystemRoles.Admin), cancellationToken);

    private Guid GetActorUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId)
            ? userId
            : throw TerritoryOperationException.Validation("Authenticated user identifier is invalid.");
    }
}
