using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Inventory.Application.Stock;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Inventory.Api;

[ApiController, Route("api/v1/warehouses"), Authorize]
public sealed class WarehousesController(WarehouseService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<WarehouseDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost, Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Save(WarehouseInput x, CancellationToken ct) => Ok(new { code = await svc.SaveAsync(x, ct) });
    [HttpPut("{code}"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Update(string code, WarehouseInput x, CancellationToken ct) => Ok(new { code = await svc.SaveAsync(x with { Code = code }, ct) });
}

[ApiController, Route("api/v1/batches"), Authorize(Roles = "Admin,Operator,WarehouseKeeper,SalesManager")]
public sealed class BatchesController(BatchService svc) : ControllerBase
{
    [HttpGet] public Task<PagedResult<BatchDto>> Search([FromQuery] int page = 1, [FromQuery] int size = 50, [FromQuery] string? search = null, [FromQuery] string? warehouse = null, [FromQuery] string? sku = null, [FromQuery] string? state = null, CancellationToken ct = default)
        => svc.SearchAsync(new PageRequest(page, size, search), warehouse, sku, state, ct);
}

[ApiController, Route("api/v1/goods-receipts"), Authorize(Roles = "Admin,WarehouseKeeper")]
public sealed class GoodsReceiptsController(GoodsReceiptService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<ReceiptDto>> List([FromQuery] string? warehouse, [FromQuery] string? status, CancellationToken ct) => svc.ListAsync(warehouse, status, ct);
    [HttpPost] public async Task<IActionResult> Create(ReceiptInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
    [HttpPost("{code}/approve"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Approve(string code, CancellationToken ct) { await svc.ApproveAsync(code, ct); return NoContent(); }
    [HttpPost("{code}/reject"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Reject(string code, CancellationToken ct) { await svc.RejectAsync(code, ct); return NoContent(); }
}

[ApiController, Route("api/v1/labels"), Authorize(Roles = "Admin,Operator,WarehouseKeeper")]
public sealed class LabelsController(LabelService svc) : ControllerBase
{
    [HttpGet("{code}")] public Task<LabelLookupDto> Lookup(string code, CancellationToken ct) => svc.LookupAsync(code, ct);
}

[ApiController, Route("api/v1/transfers"), Authorize(Roles = "Admin,WarehouseKeeper")]
public sealed class TransfersController(TransferService svc) : ControllerBase
{
    public sealed record ReceiveBody(string LabelCode);
    [HttpGet] public Task<IReadOnlyList<TransferDto>> List(CancellationToken ct) => svc.RecentAsync(ct);
    /// <summary>Creates a replenishment transfer (central → regional) and ships it: status "Đang vận chuyển". Rotation transfers are created by approving a proposal.</summary>
    [HttpPost] public async Task<IActionResult> Create(ReplenishInput x, CancellationToken ct) => Ok(new { code = await svc.CreateReplenishmentAsync(x, ct) });
    [HttpPost("{code}/receive")] public async Task<IActionResult> Receive(string code, ReceiveBody b, CancellationToken ct) { await svc.ReceiveAsync(code, b.LabelCode, ct); return NoContent(); }
}

[ApiController, Route("api/v1/disposals"), Authorize(Roles = "Admin,WarehouseKeeper")]
public sealed class DisposalsController(DisposalService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<DisposalDto>> List([FromQuery] string? warehouse, CancellationToken ct) => svc.ListAsync(warehouse, ct);
    [HttpPost] public async Task<IActionResult> Create(DisposalInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
}
