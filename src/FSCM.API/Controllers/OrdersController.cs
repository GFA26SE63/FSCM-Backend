using System.Security.Claims;
using FSCM.API.Contracts.Orders;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet]
    public Task<IReadOnlyCollection<OrderSummaryResult>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? retailerId,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), status, retailerId, cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id:guid}")]
    public Task<OrderDetailResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id:guid}/allocation-trace")]
    public Task<OrderTraceResult> Trace(Guid id, CancellationToken cancellationToken) =>
        service.GetTraceAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAssistedOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(
            request.ClientOrderKey,
            request.RetailerId,
            request.PaymentMethod,
            request.RequestedDeliveryDate,
            request.DeliveryAddress,
            request.Note,
            request.Lines.Select(item => new CreateOrderLineData(item.ProductId, item.Quantity)).ToArray()),
            GetActorUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Operator)]
    [HttpPost("{id:guid}/approve")]
    public Task<OrderDetailResult> Approve(
        Guid id,
        OrderDecisionRequest request,
        CancellationToken cancellationToken) =>
        service.ApproveAsync(id, request.ExpectedVersion, GetActorUserId(), cancellationToken);

    [Authorize(Roles = SystemRoles.Operator)]
    [HttpPost("{id:guid}/reject")]
    public Task<OrderDetailResult> Reject(
        Guid id,
        RejectOrderRequest request,
        CancellationToken cancellationToken) =>
        service.RejectAsync(id, request.ExpectedVersion, request.Reason,
            GetActorUserId(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Sales}")]
    [HttpPost("{id:guid}/cancel")]
    public Task<OrderDetailResult> Cancel(
        Guid id,
        CancelOrderRequest request,
        CancellationToken cancellationToken) =>
        service.CancelAsync(id, request.ExpectedVersion, GetActorUserId(), GetActorRole(),
            request.Reason, cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw OrderOperationException.Validation("Authenticated user role is invalid.");
}
