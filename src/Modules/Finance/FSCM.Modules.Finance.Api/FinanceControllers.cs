using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Application;
using FSCM.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Finance.Api;

[ApiController, Route("api/v1/receivables"), Authorize(Roles = "Admin,Operator,Sales,Retailer,SalesManager")]
public sealed class ReceivablesController(ReceivableService svc) : ControllerBase
{
    /// <summary>?status=Unpaid|Paid|Overdue. Sales sees their own retailers' debts, Retailer sees their own.</summary>
    [HttpGet] public Task<PagedResult<ReceivableDto>> List([FromQuery] int page = 1, [FromQuery] int size = 20, [FromQuery] string? search = null, [FromQuery] string? status = null, CancellationToken ct = default)
        => svc.ListAsync(new PageRequest(page, size, search), status, ct);

    [HttpPost("{orderCode}/collect"), Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Collect(string orderCode, CancellationToken ct) { await svc.CollectAsync(orderCode, ct); return NoContent(); }
}
