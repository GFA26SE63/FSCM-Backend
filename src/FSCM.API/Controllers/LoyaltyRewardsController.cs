using FSCM.API.Contracts.Loyalty;
using FSCM.API.Infrastructure;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/loyalty/rewards")]
public sealed class LoyaltyRewardsController(RewardService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<RewardResult>> List(
        [FromQuery] Guid? programId, [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) => service.ListAsync(programId, includeInactive, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<RewardResult> Get(Guid id, CancellationToken cancellationToken) => service.GetAsync(id, cancellationToken);

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateRewardRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(request.LoyaltyProgramId, request.Name, request.Description,
            request.ImageUrl, request.RewardType, request.RequiredPoints, request.DiscountAmount,
            request.DiscountPercent, request.AvailableQuantity), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public Task<RewardResult> Update(Guid id, UpdateRewardRequest request, CancellationToken cancellationToken) =>
        service.UpdateAsync(id, new(request.Name, request.Description, request.ImageUrl, request.RewardType,
            request.RequiredPoints, request.DiscountAmount, request.DiscountPercent, request.AvailableQuantity), cancellationToken);

    [Authorize]
    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(ImageUploadLimits.MaxRequestBytes)]
    [ProducesResponseType<RewardResult>(StatusCodes.Status200OK)]
    public async Task<RewardResult> SetImage(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        await using var upload = await file.ToImageUploadAsync(cancellationToken);
        return await service.SetImageAsync(id, upload, cancellationToken);
    }

    [Authorize]
    [HttpPost("{id:guid}/activate")]
    public Task<RewardResult> Activate(Guid id, CancellationToken cancellationToken) => service.ActivateAsync(id, cancellationToken);

    [Authorize]
    [HttpPost("{id:guid}/deactivate")]
    public Task<RewardResult> Deactivate(Guid id, CancellationToken cancellationToken) => service.DeactivateAsync(id, cancellationToken);
}
