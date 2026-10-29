using System.Security.Claims;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Promotions;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Sales}")]
[Route("api/v1/pricing")]
public sealed class PricingController(PromotionService service, OrderService orderService) : ControllerBase
{
    [HttpGet("snapshot")]
    [ProducesResponseType<PriceSnapshotResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PriceSnapshotResult> Snapshot(
        [FromQuery] Guid productId, [FromQuery] decimal quantity,
        [FromQuery] Guid? retailerId,
        [FromQuery] DateTimeOffset? at, CancellationToken cancellationToken)
    {
        var actorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");
        var actorRole = User.FindFirstValue(ClaimTypes.Role)
            ?? throw OrderOperationException.Validation("Authenticated user role is invalid.");
        await orderService.EnsurePricingScopeAsync(retailerId, actorUserId, actorRole, cancellationToken);
        return await service.CalculateIndicativePriceAsync(
            productId, quantity, retailerId, at, cancellationToken);
    }
}
