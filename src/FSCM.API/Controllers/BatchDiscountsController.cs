using System.Security.Claims;
using FSCM.API.Contracts.Promotions;
using FSCM.Application.Features.Promotions;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/batch-discounts")]
public sealed class BatchDiscountsController(PromotionService service) : ControllerBase
{
    [Authorize(Roles = SystemRoles.Admin)]
    [HttpGet("suggestions")]
    public Task<IReadOnlyCollection<BatchDiscountSuggestionResult>> Suggestions(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId,
        CancellationToken cancellationToken) =>
        service.ListBatchSuggestionsAsync(warehouseId, productId, cancellationToken);

    [HttpGet]
    public Task<IReadOnlyCollection<BatchDiscountResult>> List(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId,
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListBatchDiscountsAsync(warehouseId, productId, includeInactive, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<BatchDiscountResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetBatchDiscountAsync(id, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("batches/{batchId:guid}")]
    public async Task<IActionResult> Apply(Guid batchId, ApplyBatchDiscountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ApplyBatchDiscountAsync(batchId,
            new(request.DiscountPercent, request.StartAt, request.EndAt), ActorId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/remove")]
    public Task<BatchDiscountResult> Remove(Guid id, CancellationToken cancellationToken) =>
        service.RemoveBatchDiscountAsync(id, ActorId(), cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw PromotionOperationException.Validation("Authenticated user identifier is invalid.");
}
