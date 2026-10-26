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

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("orders/{orderId:guid}/adjust")]
    public Task<PaymentResult> Adjust(
        Guid orderId,
        AdjustReceivableRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.AdjustReceivableAsync(orderId,
            new(request.Amount, request.Reason, request.ExpectedVersion),
            operationKey, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpPost("orders/{orderId:guid}/payos-checkout")]
    public Task<PayOsCheckoutResult> CreatePayOsCheckout(
        Guid orderId,
        CreatePayOsCheckoutRequest request,
        CancellationToken cancellationToken) =>
        service.CreatePayOsCheckoutAsync(orderId, new(request.ExpectedVersion),
            GetActorUserId(), GetActorRole(), cancellationToken);

    [AllowAnonymous]
    [HttpPost("payos/webhook")]
    public Task<PayOsWebhookResult> PayOsWebhook(
        PayOsWebhookRequest request,
        CancellationToken cancellationToken) =>
        service.HandlePayOsWebhookAsync(new(
            request.Code,
            request.Desc,
            request.Success,
            new(
                request.Data.OrderCode,
                request.Data.Amount,
                request.Data.Description,
                request.Data.AccountNumber,
                request.Data.Reference,
                request.Data.TransactionDateTime,
                request.Data.Currency,
                request.Data.PaymentLinkId,
                request.Data.Code,
                request.Data.Desc,
                request.Data.CounterAccountBankId,
                request.Data.CounterAccountBankName,
                request.Data.CounterAccountName,
                request.Data.CounterAccountNumber,
                request.Data.VirtualAccountName,
                request.Data.VirtualAccountNumber),
            request.Signature), cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw PaymentOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw PaymentOperationException.Validation("Authenticated user role is invalid.");
}
