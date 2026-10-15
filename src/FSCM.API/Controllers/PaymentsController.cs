using System.Security.Claims;
using FSCM.API.Contracts.Payments;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.Retailer}")]
[Route("api/v1/payments")]
[Route("api/v1/receivables")]
public sealed class PaymentsController(PaymentService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<PaymentResult>> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), status, cancellationToken);

    [HttpGet("orders/{orderId:guid}")]
    public Task<PaymentResult> GetByOrder(Guid orderId, CancellationToken cancellationToken) =>
        service.GetByOrderAsync(orderId, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Operator},{SystemRoles.Sales}")]
    [HttpPost("orders/{orderId:guid}/collect")]
    public Task<PaymentResult> Collect(
        Guid orderId,
        CollectPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.CollectAsync(orderId,
            new(request.CollectionMethod, request.ExpectedVersion, request.Note),
            operationKey, GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw PaymentOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw PaymentOperationException.Validation("Authenticated user role is invalid.");
}
