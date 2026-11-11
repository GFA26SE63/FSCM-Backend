using System.Security.Claims;
using FSCM.API.Contracts.Promotions;
using FSCM.Application.Features.Promotions;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/promotions")]
public sealed class PromotionsController(PromotionService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<PromotionResult>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListAsync(includeInactive, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<PromotionResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetAsync(id, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("campaigns")]
    public Task<IReadOnlyCollection<PromotionResult>> CreateCampaignGroup(
        CreateCampaignGroupRequest request, CancellationToken cancellationToken)
    {
        if (request.ProductRules is null || request.ProductRules.Any(rule => rule is null))
            throw PromotionOperationException.Validation("Product rules cannot be null.");
        return service.CreateCampaignGroupAsync(new(request.RequestId, request.CampaignMode, request.Name,
            request.MinimumQuantity, request.DiscountPercent, request.StartAt, request.EndAt,
            request.ProductIds, request.CategoryIds, request.RetailerIds,
            request.ProductRules.Select(rule => new CampaignProductRuleData(rule.ProductId, rule.MinimumQuantity, rule.DiscountPercent)).ToArray(),
            request.ActivateOnSave), ActorId(), cancellationToken);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(CreatePromotionRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(request.Name, request.MinimumQuantity,
            request.DiscountPercent, request.StartAt, request.EndAt, request.ProductIds,
            request.CategoryIds, request.RetailerIds), ActorId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{id:guid}")]
    public Task<PromotionResult> Update(Guid id, UpdatePromotionRequest request,
        CancellationToken cancellationToken) =>
        service.UpdateAsync(id, new(request.Name, request.MinimumQuantity,
            request.DiscountPercent, request.StartAt, request.EndAt, request.ProductIds,
            request.CategoryIds, request.RetailerIds), ActorId(), cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/activate")]
    public Task<PromotionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        service.ActivateAsync(id, ActorId(), cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/deactivate")]
    public Task<PromotionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        service.DeactivateAsync(id, ActorId(), cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw PromotionOperationException.Validation("Authenticated user identifier is invalid.");
}
