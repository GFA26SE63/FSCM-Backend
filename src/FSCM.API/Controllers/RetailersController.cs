using System.Security.Claims;
using FSCM.API.Contracts.Retailers;
using FSCM.Application.Features.Retailers;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales}")]
[Route("api/v1/retailers")]
public sealed class RetailersController(RetailerService service) : ControllerBase
{
    [HttpGet]
    public Task<RetailerPage> List(
        [FromQuery] string? status, [FromQuery] string? search, [FromQuery] string? scope,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        service.SearchAsync(GetActorUserId(), GetActorRole(), status, search, scope, page, pageSize, cancellationToken);

    [HttpGet("{retailerId:guid}")]
    public Task<RetailerDetails> Get(Guid retailerId, CancellationToken cancellationToken) =>
        service.GetAsync(retailerId, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpPost]
    public async Task<IActionResult> Declare(DeclareRetailerRequest request, CancellationToken cancellationToken)
    {
        var result = await service.DeclareAsync(GetActorUserId(), request.RegionId, request.SalesGroupId,
            request.Name, request.ContactName, request.Phone, request.Email, request.Address,
            request.RetailerType, request.ForecastRevenue, request.StoreSizeSquareMeters,
            request.PurchasingCapacity, request.PurchaseFrequencyPerMonth, request.RequestedCreditTermDays,
            request.AssessmentNote, request.Latitude, request.Longitude, request.ProviderPlaceId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { retailerId = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpPut("{retailerId:guid}/declaration")]
    public Task<RetailerDetails> UpdateDeclaration(
        Guid retailerId, UpdateRetailerDeclarationRequest request, CancellationToken cancellationToken) =>
        service.UpdateDeclarationAsync(retailerId, GetActorUserId(), request.Name, request.ContactName,
            request.Phone, request.Email, request.Address, request.RetailerType, request.ForecastRevenue,
            request.StoreSizeSquareMeters, request.PurchasingCapacity, request.PurchaseFrequencyPerMonth,
            request.RequestedCreditTermDays, request.AssessmentNote,
            request.Latitude, request.Longitude, request.ProviderPlaceId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{retailerId:guid}/review")]
    public Task<RetailerDetails> Review(
        Guid retailerId, ReviewRetailerRequest request, CancellationToken cancellationToken) =>
        service.ReviewAsync(retailerId, GetActorUserId(), request.Approve,
            request.CreditTermDays, request.Reason, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPatch("{retailerId:guid}/status")]
    public Task<RetailerDetails> UpdateStatus(
        Guid retailerId, UpdateRetailerStatusRequest request, CancellationToken cancellationToken) =>
        service.SetActiveAsync(retailerId, request.IsActive, cancellationToken);

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpGet("{retailerId:guid}/order-eligibility")]
    public Task<RetailerOrderEligibility> CheckOrderEligibility(
        Guid retailerId, CancellationToken cancellationToken) =>
        service.CheckOrderEligibilityAsync(GetActorUserId(), retailerId, cancellationToken);

    private Guid GetActorUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId)
            ? userId
            : throw RetailerOperationException.Validation("Authenticated user identifier is invalid.");
    }

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw RetailerOperationException.Validation("Authenticated user role is invalid.");
}
