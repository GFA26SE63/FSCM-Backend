using System.Security.Claims;
using FSCM.API.Contracts.InternalTransfers;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[NonController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/internal-transfers")]
public sealed class InternalTransfersController(InternalTransferService service) : ControllerBase
{
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpGet("suggestions")]
    public Task<IReadOnlyCollection<InternalTransferSuggestionResult>> Suggestions(
        [FromQuery] Guid? destinationWarehouseId, [FromQuery] Guid? productId,
        CancellationToken cancellationToken) =>
        service.GetSuggestionsAsync(destinationWarehouseId, productId, cancellationToken);

    [HttpGet]
    public Task<IReadOnlyCollection<InternalTransferResult>> List(
        [FromQuery] Guid? warehouseId, [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), warehouseId, status, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<InternalTransferResult> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetAsync(id, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateInternalTransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new(request.SourceWarehouseId, request.DestinationWarehouseId,
            request.Lines.Select(item => new CreateInternalTransferLineData(
                item.SourceBatchId, item.Quantity, item.Note)).ToArray(), request.Note),
            operationKey, GetActorUserId(), GetActorRole(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id:guid}/approve")]
    public Task<InternalTransferResult> Approve(
        Guid id, TransferDecisionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.ApproveAsync(id, request.ExpectedVersion, operationKey, GetActorUserId(), cancellationToken);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id:guid}/reject")]
    public Task<InternalTransferResult> Reject(
        Guid id, RejectInternalTransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.RejectAsync(id, request.ExpectedVersion, operationKey, GetActorUserId(), request.Reason, cancellationToken);

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost("{id:guid}/dispatch")]
    public Task<InternalTransferResult> Dispatch(
        Guid id, DispatchInternalTransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.DispatchAsync(id, new(request.ExpectedVersion,
            request.Lines.Select(item => new DispatchInternalTransferLineData(
                item.TransferItemId, item.QrCodes)).ToArray()), operationKey,
            GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost("{id:guid}/receive")]
    public Task<InternalTransferResult> Receive(
        Guid id, ReceiveInternalTransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.ReceiveAsync(id, new(request.ExpectedVersion, request.QrCodes), operationKey,
            GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw InternalTransferOperationException.Validation("Authenticated user identifier is invalid.");
    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw InternalTransferOperationException.Validation("Authenticated user role is invalid.");
}
