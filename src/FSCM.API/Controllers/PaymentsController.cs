using System.Security.Claims;
using FSCM.API.Contracts.Payments;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Sales}")]
[Route("api/v1/payments")]
public sealed class PaymentsController(PaymentService service) : ControllerBase
{
    [HttpGet("{paymentId:guid}")]
    public Task<PaymentResult> Get(Guid paymentId, CancellationToken cancellationToken) =>
        service.GetAsync(paymentId, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpGet]
    public Task<IReadOnlyCollection<PaymentResult>> ListByOrder(
        [FromQuery] Guid salesOrderId, CancellationToken cancellationToken) =>
        service.ListByOrderAsync(salesOrderId, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpPost]
    public async Task<IActionResult> Record(RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RecordAsync(GetActorUserId(), GetActorRole(),
            new RecordPaymentData(request.SalesOrderId, request.Method, request.Amount, request.MarkPaid, request.Note),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { paymentId = result.Id }, result);
    }

    [HttpPost("{paymentId:guid}/confirm")]
    public Task<PaymentResult> Confirm(
        Guid paymentId, ConfirmPaymentRequest request, CancellationToken cancellationToken) =>
        service.ConfirmAsync(paymentId, GetActorUserId(), GetActorRole(), request.Note, cancellationToken);

    [HttpPost("{paymentId:guid}/cancel")]
    public Task<PaymentResult> Cancel(
        Guid paymentId, CancelPaymentRequest request, CancellationToken cancellationToken) =>
        service.CancelAsync(paymentId, GetActorUserId(), GetActorRole(), request.Reason, cancellationToken);

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw PaymentOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw PaymentOperationException.Validation("Authenticated user role is invalid.");
}
