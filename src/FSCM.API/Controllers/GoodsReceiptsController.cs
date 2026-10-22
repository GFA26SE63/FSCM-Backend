using System.Security.Claims;
using FSCM.API.Contracts.Inventory;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/goods-receipts")]
public sealed class GoodsReceiptsController(InventoryService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<GoodsReceiptResult>> List(
        [FromQuery] Guid? warehouseId, CancellationToken cancellationToken) =>
        service.ListReceiptsAsync(GetActorUserId(), GetActorRole(), warehouseId, cancellationToken);

    [HttpGet("{receiptId:guid}")]
    public Task<GoodsReceiptResult> Get(Guid receiptId, CancellationToken cancellationToken) =>
        service.GetReceiptAsync(receiptId, GetActorUserId(), GetActorRole(), cancellationToken);

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost]
    public async Task<IActionResult> Receive(
        ReceiveGoodsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await service.ReceiveAsync(GetActorUserId(), GetActorRole(), request.WarehouseId,
            idempotencyKey, request.Lines.Select(item => new ReceiveGoodsLineData(item.ProductId,
                item.SourceLotCode, item.Quantity, item.ExpirationDate)).ToArray(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { receiptId = result.Id }, result);
    }

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw InventoryOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw InventoryOperationException.Validation("Authenticated user role is invalid.");
}
