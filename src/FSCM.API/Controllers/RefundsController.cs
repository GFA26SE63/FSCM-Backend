using System.Security.Claims;
using FSCM.API.Contracts.Refunds;
using FSCM.Application.Features.Refunds;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.Retailer}")]
[Route("api/v1/refunds")]
public sealed class RefundsController(RefundService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<RefundResult>> List([FromQuery] Guid? orderId, [FromQuery] string? status,
        CancellationToken ct) => service.ListAsync(orderId, status, UserId(), Role(), ct);

    [HttpGet("{id:guid}")]
    public Task<RefundResult> Get(Guid id, CancellationToken ct) => service.GetAsync(id, UserId(), Role(), ct);

    [HttpPost]
    [Authorize(Roles = SystemRoles.Operator)]
    public async Task<IActionResult> Create(CreateRefundRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(new(request.SalesOrderId, request.Amount, request.Recipient, request.Reason),
            Request.Headers["Idempotency-Key"].ToString(), UserId(), Role(), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/evidence")]
    [Authorize(Roles = SystemRoles.Operator)]
    public Task<RefundResult> Evidence(Guid id, SubmitRefundEvidenceRequest request, CancellationToken ct) =>
        service.SubmitEvidenceAsync(id, new(request.ExpectedVersion, request.TransferredAt,
            request.TransactionReference, request.EvidenceReference), Request.Headers["Idempotency-Key"].ToString(), UserId(), Role(), ct);

    [HttpPost("{id:guid}/verify")]
    [Authorize(Roles = SystemRoles.Admin)]
    public Task<RefundResult> Verify(Guid id, VerifyRefundRequest request, CancellationToken ct) =>
        service.VerifyAsync(id, new(request.ExpectedVersion, request.Note),
            Request.Headers["Idempotency-Key"].ToString(), UserId(), Role(), ct);

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw RefundOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw RefundOperationException.Validation("Authenticated user role is invalid.");
}
