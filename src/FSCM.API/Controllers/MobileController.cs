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
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MobileSnapshotResult>> Snapshot(CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(ActorId(), cancellationToken);
        var etag = $"\"{result.Version}\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, no-cache";
        if (Request.Headers.IfNoneMatch.Any(value => (value ?? string.Empty).Split(',').Any(tag =>
                tag.Trim() is "*" || string.Equals(tag.Trim(), etag, StringComparison.Ordinal))))
            return StatusCode(StatusCodes.Status304NotModified);
        return Ok(result);
    }

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");
}
