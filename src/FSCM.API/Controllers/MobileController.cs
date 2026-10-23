using System.Security.Claims;
using FSCM.Application.Features.MobileSnapshots;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Sales)]
[Route("api/v1/mobile")]
public sealed class MobileController(MobileSnapshotService service) : ControllerBase
{
    [HttpGet("snapshot")]
    [ProducesResponseType<MobileSnapshotResult>(StatusCodes.Status200OK)]
    public Task<MobileSnapshotResult> Snapshot(
        [FromQuery] string? sinceVersion, CancellationToken cancellationToken) =>
        service.GetAsync(ActorId(), sinceVersion, cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");
}
