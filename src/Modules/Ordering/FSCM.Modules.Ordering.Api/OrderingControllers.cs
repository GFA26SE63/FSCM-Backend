using System.Text.Json;
using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Ordering.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Ordering.Api;

[ApiController, Route("api/v1/orders"), Authorize]
public sealed class OrdersController(OrderPlacementService placement, OrderWorkflowService workflow, OrderQueryService query) : ControllerBase
{
    [HttpGet] public Task<PagedResult<OrderSummaryDto>> Search([FromQuery] int page = 1, [FromQuery] int size = 20, [FromQuery] string? search = null, [FromQuery] string? status = null, [FromQuery] string? retailer = null, CancellationToken ct = default)
        => query.SearchAsync(new PageRequest(page, size, search), status, retailer, ct);
    [HttpGet("{code}")] public Task<OrderDetailDto> Get(string code, CancellationToken ct) => query.GetAsync(code, ct);
    /// <summary>Allocation explanation: per line the B1–B4 tables, scores and the estimated delivery cost per warehouse.</summary>
    [HttpGet("{code}/allocation-trace")] public async Task<JsonElement> Trace(string code, CancellationToken ct) => await query.TraceAsync(code, ct);

    [HttpPost, Authorize(Roles = "Admin,Operator,Sales")] public async Task<IActionResult> Create(CreateOrderInput x, CancellationToken ct) => Ok(await placement.CreateAsync(x, ct));
    /// <summary>Batch of offline orders; body is processed in device order, every item idempotent by clientRequestId.</summary>
    [HttpPost("sync"), Authorize(Roles = "Sales")] public async Task<IActionResult> Sync(IReadOnlyList<OfflineOrderInput> orders, CancellationToken ct) => Ok(await placement.SyncAsync(orders, ct));

    [HttpPost("{code}/approve"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Approve(string code, CancellationToken ct) { await workflow.ApproveAsync(code, ct); return NoContent(); }
    [HttpPost("{code}/reject"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Reject(string code, RejectInput x, CancellationToken ct) { await workflow.RejectAsync(code, x.Reason, ct); return NoContent(); }
    [HttpPost("{code}/cancel"), Authorize(Roles = "Admin,Operator,Sales,Retailer")] public async Task<IActionResult> Cancel(string code, CancellationToken ct) { await workflow.CancelAsync(code, ct); return NoContent(); }
    [HttpPost("{code}/dispatch"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Dispatch(string code, DispatchInput x, CancellationToken ct) { await workflow.DispatchAsync(code, x, ct); return NoContent(); }
    [HttpPost("{code}/confirm-receipt"), Authorize(Roles = "Retailer")] public async Task<IActionResult> ConfirmReceipt(string code, ConfirmReceiptInput x, CancellationToken ct) { await workflow.ConfirmReceiptAsync(code, x.QrToken, ct); return NoContent(); }
}

[ApiController, Route("api/v1/picking-lists"), Authorize(Roles = "Admin,Operator,WarehouseKeeper")]
public sealed class PickingListsController(PickingService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<PickingListDto>> List([FromQuery] string? warehouse, [FromQuery] string? status, CancellationToken ct) => svc.ListAsync(warehouse, status, ct);
    [HttpGet("{code}")] public Task<PickingListDto> Get(string code, CancellationToken ct) => svc.GetAsync(code, ct);
    [HttpPost("{code}/scan"), Authorize(Roles = "Admin,WarehouseKeeper")] public Task<PickingListDto> Scan(string code, ScanInput x, CancellationToken ct) => svc.ScanAsync(code, x, ct);
}

[ApiController, Route("api/v1/complaints"), Authorize]
public sealed class ComplaintsController(ComplaintService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<ComplaintDto>> List([FromQuery] string? status, CancellationToken ct) => svc.ListAsync(status, ct);
    [HttpPost, Authorize(Roles = "Retailer,Operator,Admin")] public async Task<IActionResult> Create(ComplaintInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
    [HttpPost("{code}/resolve"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Resolve(string code, ResolveComplaintInput x, CancellationToken ct) { await svc.ResolveAsync(code, x, ct); return NoContent(); }
}

[ApiController, Route("api/v1/allocation-settings"), Authorize(Roles = "Admin")]
public sealed class AllocationSettingsController(AllocationSettingsService svc) : ControllerBase
{
    [HttpGet] public Task<AllocationSettingsDto> Get(CancellationToken ct) => svc.GetAsync(ct);
    [HttpPut] public async Task<IActionResult> Put(AllocationSettingsDto x, CancellationToken ct) { await svc.SaveAsync(x, ct); return NoContent(); }
}

[ApiController, Route("api/v1/sync"), Authorize]
public sealed class SyncController(SyncSnapshotService svc) : ControllerBase
{
    /// <summary>Mobile cache: retailers of the Sales' area (changed since), published products + prices, active promotions, available stock summary.</summary>
    [HttpGet("snapshot")] public Task<SyncSnapshotDto> Snapshot([FromQuery] DateTime? since, CancellationToken ct) => svc.GetAsync(since, ct);
}
