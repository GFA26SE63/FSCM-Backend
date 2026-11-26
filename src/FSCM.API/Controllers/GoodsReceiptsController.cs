using System.Security.Claims;
using FSCM.API.Contracts.Inventory;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
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

    [Authorize]
    [HttpPost]
    public IActionResult Create(CreateGoodsReceiptRequest request) =>
        Conflict(new { code = "FACTORY_PALLET_REQUIRED", message = "Tạo phiếu nhập từ nhà máy tại /warehouse-work và quét pallet đã phát hành. Kho không sinh batch hoặc mã carton nhà máy." });

    [Authorize]
    [HttpPost("{receiptId:guid}/approve")]
    public Task<GoodsReceiptResult> Approve(Guid receiptId, CancellationToken cancellationToken) =>
        service.ApproveReceiptAsync(receiptId, GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw InventoryOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw InventoryOperationException.Validation("Authenticated user role is invalid.");
}
