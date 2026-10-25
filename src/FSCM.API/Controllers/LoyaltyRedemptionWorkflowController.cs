using System.Security.Claims;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
[Route("api/v1/loyalty/redemptions")]
public sealed class LoyaltyRedemptionWorkflowController(RedemptionWorkflowService service) : ControllerBase
{
    [HttpPost("{id:guid}/approve")]
    public Task<RedemptionResult> Approve(Guid id, CancellationToken ct) => service.ApproveAsync(id, ActorId(), ct);
    [HttpPost("{id:guid}/reject")]
    public Task<RedemptionResult> Reject(Guid id, CancellationToken ct) => service.RejectAsync(id, ActorId(), ct);
    [HttpPost("{id:guid}/deliver")]
    public Task<RedemptionResult> Deliver(Guid id, CancellationToken ct) => service.DeliverAsync(id, ActorId(), ct);
    [HttpPost("{id:guid}/complete")]
    public Task<RedemptionResult> Complete(Guid id, CancellationToken ct) => service.CompleteAsync(id, ActorId(), ct);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw LoyaltyOperationException.Validation("Authenticated user identifier is invalid.");
}
