using System.Security.Claims;
using FSCM.Application.Features.Territory;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Sales)]
[Route("api/v1/territory-access")]
public sealed class TerritoryAccessController(TerritoryService service) : ControllerBase
{
    [HttpGet("retailers/{retailerId:guid}/order-eligibility")]
    public Task<TerritoryOrderAccessResult> CheckOrderEligibility(
        Guid retailerId, CancellationToken cancellationToken)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
            throw TerritoryOperationException.Validation("Authenticated user identifier is invalid.");
        return service.CheckOrderAccessAsync(userId, retailerId, cancellationToken);
    }
}
