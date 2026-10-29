using System.Security.Claims;
using FSCM.API.Contracts.Loyalty;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/loyalty/programs")]
public sealed class LoyaltyController(LoyaltyService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<LoyaltyProgramResult>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListAsync(includeInactive, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<LoyaltyProgramResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetAsync(id, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateLoyaltyProgramRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(request.Name, request.Description,
            request.SpendAmountPerPoint, request.MinimumRedeemPoints,
            request.StartAt, request.EndAt, request.PointsExpireAt, request.RetailerIds), ActorId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{id:guid}")]
    public Task<LoyaltyProgramResult> Update(Guid id, UpdateLoyaltyProgramRequest request,
        CancellationToken cancellationToken) =>
        service.UpdateAsync(id, new(request.Name, request.Description,
            request.SpendAmountPerPoint, request.MinimumRedeemPoints,
            request.StartAt, request.EndAt, request.PointsExpireAt, request.RetailerIds), cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/activate")]
    public Task<LoyaltyProgramResult> Activate(Guid id, CancellationToken cancellationToken) =>
        service.ActivateAsync(id, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/deactivate")]
    public Task<LoyaltyProgramResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        service.DeactivateAsync(id, cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw LoyaltyOperationException.Validation("Authenticated user identifier is invalid.");
}
