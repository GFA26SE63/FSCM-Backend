using System.Security.Claims;
using FSCM.API.Contracts.ShelfLifeDiscounts;
using FSCM.Application.Features.ShelfLifeDiscounts;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/shelf-life-discounts")]
public sealed class ShelfLifeDiscountsController(ShelfLifeDiscountService service) : ControllerBase
{
    [HttpGet("current")]
    public Task<ShelfLifeDiscountTableResult> Current(CancellationToken cancellationToken) =>
        service.GetCurrentAsync(cancellationToken);

    [HttpGet("history")]
    public Task<IReadOnlyCollection<ShelfLifeDiscountTableResult>> History(CancellationToken cancellationToken) =>
        service.ListHistoryAsync(cancellationToken);

    [HttpGet("versions/{version:int}")]
    public Task<ShelfLifeDiscountTableResult> Version(int version, CancellationToken cancellationToken) =>
        service.GetVersionAsync(version, cancellationToken);

    /// <summary>
    /// The tier that applies to one product at a remaining percentage. Pass <c>at</c> to read the
    /// version that was in force then, so a run priced earlier can be explained.
    /// </summary>
    [HttpGet("match")]
    public Task<ShelfLifeDiscountMatch> Match(
        [FromQuery] Guid productId,
        [FromQuery] Guid productCategoryId,
        [FromQuery] decimal remainingPercent,
        [FromQuery] decimal baseNetUnitPrice,
        [FromQuery] DateTimeOffset? at,
        CancellationToken cancellationToken) =>
        service.MatchAsync(productId, productCategoryId, remainingPercent, baseNetUnitPrice, at, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Publish(
        PublishShelfLifeDiscountTableRequest request, CancellationToken cancellationToken)
    {
        var result = await service.PublishAsync(
            new PublishShelfLifeDiscountTableData(
                request.EffectiveFrom,
                request.Note,
                request.Tiers.Select(item => new ShelfLifeDiscountTierData(
                    item.Scope, item.ProductId, item.ProductCategoryId,
                    item.MinRemainingPercent, item.MaxRemainingPercent,
                    item.DiscountType, item.DiscountValue)).ToArray()),
            ActorId(),
            cancellationToken);
        return CreatedAtAction(nameof(Version), new { version = result.Version }, result);
    }

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw ShelfLifeDiscountOperationException.Validation("Authenticated user identifier is invalid.");
}
