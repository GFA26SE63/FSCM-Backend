using System.Security.Claims;
using FSCM.API.Contracts.TransitTables;
using FSCM.Application.Features.TransitTables;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transit-tables")]
public sealed class TransitTablesController(TransitTableService service) : ControllerBase
{
    [HttpGet("current")]
    public Task<TransitTableResult> Current(CancellationToken cancellationToken) =>
        service.GetCurrentAsync(cancellationToken);

    [HttpGet("history")]
    public Task<IReadOnlyCollection<TransitTableResult>> History(CancellationToken cancellationToken) =>
        service.ListHistoryAsync(cancellationToken);

    [HttpGet("versions/{version:int}")]
    public Task<TransitTableResult> Version(int version, CancellationToken cancellationToken) =>
        service.GetVersionAsync(version, cancellationToken);

    /// <summary>
    /// The band serving one distance, for the shared ETA function and delivery cost estimation.
    /// Pass <c>at</c> to read the version that was in force then instead of the current one.
    /// </summary>
    [HttpGet("lookup")]
    public Task<TransitLookupResult> Lookup(
        [FromQuery] int distanceMeters, [FromQuery] DateTimeOffset? at,
        CancellationToken cancellationToken) =>
        service.LookupAsync(distanceMeters, at, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Publish(
        PublishTransitTableRequest request, CancellationToken cancellationToken)
    {
        var result = await service.PublishAsync(
            new PublishTransitTableData(
                request.MaxServingDistanceMeters,
                request.EffectiveFrom,
                request.Note,
                request.Bands.Select(item => new TransitBandData(
                    item.MinDistanceMeters, item.MaxDistanceMeters,
                    item.HandlingMinutes, item.TransitMinutes, item.EstimatedCostAmount)).ToArray()),
            ActorId(),
            cancellationToken);
        return CreatedAtAction(nameof(Version), new { version = result.Version }, result);
    }

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw TransitTableOperationException.Validation("Authenticated user identifier is invalid.");
}
