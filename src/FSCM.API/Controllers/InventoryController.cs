using System.Security.Claims;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/inventory")]
public sealed class InventoryController(InventoryService service, IOptions<DateTrackerOptions> dateTrackerOptions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<BatchInventoryResult>> List(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId,
        [FromQuery] bool includeExpired = false, CancellationToken cancellationToken = default) =>
        service.ListInventoryAsync(GetActorUserId(), GetActorRole(), warehouseId, productId,
            includeExpired, cancellationToken);

    [HttpGet("batches/{batchId:guid}")]
    public Task<BatchInventoryResult> GetBatch(Guid batchId, CancellationToken cancellationToken) =>
        service.GetBatchAsync(batchId, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpGet("history")]
    public Task<IReadOnlyCollection<InventoryHistoryResult>> History(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? batchId,
        CancellationToken cancellationToken) =>
        service.ListHistoryAsync(GetActorUserId(), GetActorRole(), warehouseId, batchId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpGet("expiry/health")]
    public Task<InventoryExpiryHealthResult> ExpiryHealth(CancellationToken cancellationToken)
    {
        var options = dateTrackerOptions.Value;
        return service.GetExpiryHealthAsync(
            options.Enabled, options.TimeZone, options.RunAtLocalTime, cancellationToken);
    }

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw InventoryOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw InventoryOperationException.Validation("Authenticated user role is invalid.");
}
