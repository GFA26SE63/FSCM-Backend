using System.Security.Claims;
using FSCM.API.Contracts.ManualPayments;
using FSCM.Application.Features.ManualPayments;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.Retailer}")]
[Route("api/v1/manual-payments")]
public sealed class ManualPaymentsController(ManualPaymentService service) : ControllerBase
{
    [HttpGet("{paymentId:guid}")]
    public Task<ManualPaymentResult> Get(Guid paymentId, CancellationToken cancellationToken) =>
        service.GetAsync(paymentId, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpGet]
    public Task<IReadOnlyCollection<ManualPaymentResult>> ListByOrder(
        [FromQuery] Guid salesOrderId, CancellationToken cancellationToken) =>
        service.ListByOrderAsync(salesOrderId, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpPost]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public async Task<IActionResult> Record(RecordManualPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RecordAsync(GetActorUserId(), GetActorRole(),
            new RecordManualPaymentData(request.SalesOrderId, request.Method, request.Amount, request.MarkPaid, request.Note),
            Request.Headers["Idempotency-Key"].ToString(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { paymentId = result.Id }, result);
    }

    [HttpPost("{paymentId:guid}/confirm")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public Task<ManualPaymentResult> Confirm(
        Guid paymentId, ConfirmManualPaymentRequest request, CancellationToken cancellationToken) =>
        service.ConfirmAsync(paymentId, GetActorUserId(), GetActorRole(),
            new(request.ExpectedVersion, request.Note), cancellationToken);

    [HttpPost("{paymentId:guid}/cancel")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public Task<ManualPaymentResult> Cancel(
        Guid paymentId, CancelManualPaymentRequest request, CancellationToken cancellationToken) =>
        service.CancelAsync(paymentId, GetActorUserId(), GetActorRole(),
            new(request.ExpectedVersion, request.Reason), cancellationToken);

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw ManualPaymentOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw ManualPaymentOperationException.Validation("Authenticated user role is invalid.");
}
