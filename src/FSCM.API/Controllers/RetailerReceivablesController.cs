using System.Security.Claims;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Retailer)]
[Route("api/v1/retailer/receivables")]
public sealed class RetailerReceivablesController(RetailerReceivableService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<RetailerReceivablePage>(StatusCodes.Status200OK)]
    public Task<RetailerReceivablePage> List(
        [FromQuery] string? status,
        [FromQuery] string? orderCode,
        [FromQuery] DateOnly? dueFrom,
        [FromQuery] DateOnly? dueTo,
        [FromQuery] bool? overdue,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(UserId(), new(status, orderCode, dueFrom, dueTo, overdue, page, pageSize),
            cancellationToken);

    [HttpGet("orders/{orderId:guid}")]
    [ProducesResponseType<RetailerReceivableResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<RetailerReceivableResult> GetByOrder(
        Guid orderId, CancellationToken cancellationToken) =>
        service.GetByOrderAsync(UserId(), orderId, cancellationToken);

    private Guid UserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw PaymentOperationException.Validation("Authenticated user identifier is invalid.");
}
