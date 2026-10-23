using System.Security.Claims;
using FSCM.API.Contracts.InventoryLocks;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/inventory-locks")]
public sealed class InventoryLocksController(InventoryLockService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<InventoryLockResult>> List(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? batchId, [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), warehouseId, batchId, status, cancellationToken);

    [HttpGet("{lockId:guid}")]
    public Task<InventoryLockResult> Get(Guid lockId, CancellationToken cancellationToken) =>
        service.GetAsync(lockId, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpPost]
    public async Task<IActionResult> Create(CreateInventoryLockRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(GetActorUserId(), GetActorRole(),
            new CreateInventoryLockData(request.BatchId, request.Quantity, request.Reason, request.Note),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { lockId = result.Id }, result);
    }

    [HttpPost("{lockId:guid}/release")]
    public Task<InventoryLockResult> Release(
        Guid lockId, ReleaseInventoryLockRequest request, CancellationToken cancellationToken) =>
        service.ReleaseAsync(lockId, GetActorUserId(), GetActorRole(),
            new ReleaseInventoryLockData(request.ExpectedVersion, request.Note), cancellationToken);

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw InventoryLockOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw InventoryLockOperationException.Validation("Authenticated user role is invalid.");
}
