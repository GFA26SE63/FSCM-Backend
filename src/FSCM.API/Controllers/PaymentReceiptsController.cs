using System.Security.Claims;
using FSCM.API.Contracts.Payments;
using FSCM.Application.Features.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/payment-receipts")]
public sealed class PaymentReceiptsController(PaymentReceiptService service) : ControllerBase
{
    [HttpPost("confirm")]
    public Task<PaymentReceiptResult> Confirm(ConfirmPaymentReceiptRequest request, [FromHeader(Name = "Idempotency-Key")] string operationKey, CancellationToken cancellationToken) =>
        service.ConfirmAsync(new(request.RetailerId, request.ReceivedAmount, request.Reference, request.Note, request.Allocations.Select(x => new ReceiptAllocationData(x.PaymentId, x.Amount)).ToArray()), operationKey, UserId(), Role(), cancellationToken);

    [HttpPost("{receiptId:guid}/apply-credit")]
    public Task<PaymentReceiptResult> ApplyCredit(Guid receiptId, ApplyReceiptCreditRequest request, CancellationToken cancellationToken) =>
        service.ApplyUnappliedCreditAsync(receiptId, request.ExpectedVersion, request.Allocations.Select(x => new ReceiptAllocationData(x.PaymentId, x.Amount)).ToArray(), UserId(), Role(), cancellationToken);
    [HttpPost("{receiptId:guid}/reverse")]
    public Task<PaymentReceiptResult> Reverse(Guid receiptId, ReversePaymentReceiptRequest request, CancellationToken cancellationToken) =>
        service.ReverseAsync(receiptId, request.Reason, UserId(), Role(), cancellationToken);

    // F5 step 4: suggested allocation (overdue → earliest due → earliest created → order code).
    // Read-only; the user still confirms via /confirm.
    [HttpPost("propose-allocation")]
    public Task<ReceiptAllocationProposalResult> ProposeAllocation(
        ProposeReceiptAllocationRequest request, CancellationToken cancellationToken) =>
        service.ProposeAllocationAsync(
            new(request.RetailerId, request.ReceivedAmount), UserId(), Role(), cancellationToken);

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw PaymentOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role) ?? throw PaymentOperationException.Forbidden("Authenticated user role is invalid.");
}
