using System.Security.Claims;
using FSCM.API.Contracts.Deliveries;
using FSCM.API.Contracts.Orders;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController(
    OrderService service,
    DeliveryService deliveryService,
    OrderAvailabilityService availability) : ControllerBase
{
    [HttpPost("availability")]
    public Task<OrderAvailabilityResult> Availability(
        OrderAvailabilityRequest request, CancellationToken cancellationToken) =>
        availability.CheckAsync(request.RetailerId, request.ProductIds,
            GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet]
    public Task<PagedOrderResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? retailerId,
        [FromQuery] bool createdByMe = false,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default,
        [FromQuery] int? size = null) =>
        service.ListAsync(GetActorUserId(), GetActorRole(),
            new(status, retailerId, createdByMe, search, page, size ?? pageSize), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id:guid}")]
    public Task<OrderDetailResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("by-code/{code}")]
    public Task<OrderDetailResult> GetByCode(string code, CancellationToken cancellationToken) =>
        service.GetByCodeAsync(code, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.WarehouseKeeper},{SystemRoles.Retailer}")]
    [HttpGet("{id:guid}/timeline")]
    public Task<OrderTimelineResult> Timeline(Guid id, CancellationToken cancellationToken) =>
        service.GetTimelineAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Sales},{SystemRoles.Retailer}")]
    [HttpPost]
    [ProducesResponseType<OrderDetailResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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
            cancellationToken, GetActorRole());
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Sales)]
    [HttpPost("offline")]
    [HttpPost("sync")]
    [ProducesResponseType<OrderDetailResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitOffline(
        SubmitOfflineOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitOfflineAsync(new(
            request.ClientOrderKey,
            request.RetailerId,
            request.PaymentMethod,
            request.RequestedDeliveryDate,
            request.DeliveryAddress,
            request.Note,
            request.OfflineEstimatedAmount,
            request.Lines.Select(item => new CreateOrderLineData(item.ProductId, item.Quantity)).ToArray(),
            request.SnapshotVersion,
            request.ConfirmConditionChanges,
            request.ExpectedConditions?.Select(item => new OfflineExpectedConditionData(
                item.ProductId, item.ExpectedBaseUnitPrice, item.ExpectedMlorVersion,
                item.ExpectedMlorRatio)).ToArray()),
            GetActorUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Sales}")]
    [HttpPost("{id:guid}/cancel")]
    public Task<OrderDetailResult> Cancel(
        Guid id,
        CancelOrderRequest request,
        CancellationToken cancellationToken) =>
        service.CancelAsync(id, request.ExpectedVersion, GetActorUserId(), GetActorRole(),
            request.Reason, cancellationToken);

    // Order-level dispatch and receipt were removed (Review 2): delivery is per warehouse shipment
    // (POST /api/v1/shipments/{id}/dispatch and /receipt).

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw OrderOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw OrderOperationException.Validation("Authenticated user role is invalid.");
}


public sealed record OrderAvailabilityRequest(Guid RetailerId, IReadOnlyCollection<Guid> ProductIds);
