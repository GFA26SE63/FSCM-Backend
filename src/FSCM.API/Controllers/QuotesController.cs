using System.Security.Claims;
using FSCM.API.Contracts.Quotes;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/quotes")]
public sealed class QuotesController(QuoteService service) : ControllerBase
{
    [HttpGet("orders/{orderId:guid}")]
    public Task<IReadOnlyCollection<SalesOrderQuoteResult>> ForOrder(Guid orderId, CancellationToken ct) => service.ListForOrderAsync(orderId, ActorId(), Role(), ct);

    [HttpGet("runs/{runId:guid}")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public Task<IReadOnlyCollection<SalesOrderQuoteResult>> ForRun(Guid runId, CancellationToken ct) => service.ListForRunAsync(runId, ct);

    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = SystemRoles.Retailer)]
    public Task<SalesOrderQuoteResult> Accept(Guid id, AcceptQuoteRequest request, CancellationToken ct) => service.AcceptAsync(id, request.ExpectedVersion, ActorId(), Role(), ct);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = SystemRoles.Retailer)]
    public Task<SalesOrderQuoteResult> Reject(Guid id, RejectQuoteRequest request, CancellationToken ct) => service.RejectAsync(id, request.ExpectedVersion, request.Reason, ActorId(), Role(), ct);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw QuoteOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role) ?? throw QuoteOperationException.Validation("Authenticated user role is invalid.");
}
