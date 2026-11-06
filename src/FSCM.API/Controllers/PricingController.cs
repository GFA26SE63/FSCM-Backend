using System.Security.Claims;
using FSCM.API.Contracts.Promotions;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Promotions;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Sales},{SystemRoles.Retailer}")]
[Route("api/v1/pricing")]
public sealed class PricingController(
    PromotionService service,
    OrderService orderService,
    TimeProvider timeProvider) : ControllerBase
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

    [HttpPost("cart-snapshot")]
    [ProducesResponseType<IReadOnlyCollection<PriceSnapshotResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyCollection<PriceSnapshotResult>> CartSnapshot(
        CartPriceSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RetailerId == Guid.Empty || request.Lines is null || request.Lines.Count is < 1 or > 100 ||
            request.Lines.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0 ||
                                      item.Quantity != decimal.Truncate(item.Quantity)))
        {
            throw OrderOperationException.Validation(
                "Cart pricing requires a retailer and between 1 and 100 valid carton lines.");
        }

        var actorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");
        var actorRole = User.FindFirstValue(ClaimTypes.Role)
            ?? throw OrderOperationException.Validation("Authenticated user role is invalid.");
        await orderService.EnsurePricingScopeAsync(
            request.RetailerId, actorUserId, actorRole, cancellationToken);

        var lines = request.Lines
            .GroupBy(item => item.ProductId)
            .Select(group => new PromotionPriceLineInput(
                group.Key, group.Sum(item => item.Quantity)))
            .ToArray();
        var results = await service.CalculateIndicativePricesAsync(
            lines, request.RetailerId, request.At ?? timeProvider.GetUtcNow(), cancellationToken);
        return lines.Select(item => results[item.ProductId]).ToArray();
    }
}
