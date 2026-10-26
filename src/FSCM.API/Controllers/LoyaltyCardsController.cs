using System.Security.Claims;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Retailer}")]
[Route("api/v1/loyalty/cards")]
public sealed class LoyaltyCardsController(LoyaltyCardService service) : ControllerBase
{
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpGet]
    public Task<IReadOnlyCollection<LoyaltyCardResult>> List(CancellationToken cancellationToken) =>
        service.ListAsync(ActorId(), Role(), cancellationToken);

    [HttpGet("me")]
    public Task<LoyaltyCardResult> Me(CancellationToken cancellationToken) =>
        service.GetAsync(ActorId(), Role(), null, cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpGet("{retailerId:guid}")]
    public Task<LoyaltyCardResult> Get(Guid retailerId, CancellationToken cancellationToken) =>
        service.GetAsync(ActorId(), Role(), retailerId, cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpGet("by-code/{retailerCode}")]
    public Task<LoyaltyCardResult> GetByCode(string retailerCode, CancellationToken cancellationToken) =>
        service.GetByCodeAsync(ActorId(), Role(), retailerCode, cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw LoyaltyOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw LoyaltyOperationException.Validation("Authenticated user role is invalid.");
}
