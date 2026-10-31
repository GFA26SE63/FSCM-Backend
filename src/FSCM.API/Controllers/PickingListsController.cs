using System.Security.Claims;
using FSCM.API.Contracts.Picking;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[NonController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/picking-lists")]
public sealed class PickingListsController(PickingService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<PickingListResult>> List(
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), warehouseId, status, cancellationToken);

    [HttpGet("{pickingCode}")]
    public Task<PickingListResult> Get(string pickingCode, CancellationToken cancellationToken) =>
        service.GetAsync(pickingCode, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost("{pickingCode}/scans")]
    public Task<PickingListResult> Scan(
        string pickingCode,
        ScanPickingLabelRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.ScanAsync(
            pickingCode,
            new ScanPickingLabelData(
                request.QrCode,
                request.Quantity,
                request.ExpectedPickingVersion,
                request.ExpectedLabelVersion,
                request.Splits.Select(item => new RunPickingSplitQuantityData(
                    item.AllocationRunAllocationId, item.Quantity)).ToArray()),
            operationKey,
            GetActorUserId(),
            GetActorRole(),
            cancellationToken);

    /// <summary>Marks one warehouse portion Ready only after every approved gather/split is picked.</summary>
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost("{pickingCode}/complete")]
    public Task<PickingListResult> Complete(
        string pickingCode,
        CompleteRunPickingRequest request,
        CancellationToken cancellationToken) =>
        service.CompleteAsync(pickingCode,
            new CompleteRunPickingData(request.ExpectedPickingVersion, request.ExceptionType, request.Note),
            GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw PickingOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw PickingOperationException.Validation("Authenticated user role is invalid.");
}

