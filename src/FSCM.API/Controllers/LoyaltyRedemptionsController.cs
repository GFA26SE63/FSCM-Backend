using System.Security.Claims;
using FSCM.API.Contracts.Loyalty;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Retailer},{SystemRoles.Admin},{SystemRoles.Operator}")]
[Route("api/v1/loyalty/redemptions")]
public sealed class LoyaltyRedemptionsController(RedemptionService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<RedemptionResult>> List(CancellationToken cancellationToken) =>
        IsStaff() ? service.ListForAdminAsync(cancellationToken) : service.ListAsync(ActorId(), cancellationToken);

    [HttpPost]
    public Task<RedemptionResult> Create(CreateRedemptionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.CreateAsync(ActorId(), new(request.RewardId, request.Quantity, operationKey, request.Note), cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw LoyaltyOperationException.Validation("Authenticated user identifier is invalid.");

    private bool IsStaff() => User.IsInRole(SystemRoles.Admin) || User.IsInRole(SystemRoles.Operator);
}
