using FSCM.Application.Features.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/pricing")]
public sealed class PricingController(PromotionService service) : ControllerBase
{
    [HttpGet("snapshot")]
    public Task<PriceSnapshotResult> Snapshot(
        [FromQuery] Guid productId, [FromQuery] decimal quantity,
        [FromQuery] Guid? batchId, [FromQuery] Guid? retailerId,
        [FromQuery] DateTimeOffset? at, CancellationToken cancellationToken) =>
        service.CalculatePriceAsync(productId, quantity, batchId, retailerId, at, cancellationToken);
}
