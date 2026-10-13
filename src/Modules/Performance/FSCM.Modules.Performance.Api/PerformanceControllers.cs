using FSCM.Modules.Performance.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Performance.Api;

[ApiController, Route("api/v1"), Authorize]
public sealed class PerformanceController(PerformanceQueryService query, TargetService targets) : ControllerBase
{
    /// <summary>KPI per Sales for a period (yyyy-MM / yyyy-Qn / yyyy). A Sales only receives their own row.</summary>
    [HttpGet("kpi/sales")] public Task<IReadOnlyList<SalesKpiDto>> SalesKpi([FromQuery] string? period, CancellationToken ct) => query.SalesKpiAsync(period, ct);
    [HttpPut("kpi/targets"), Authorize(Roles = "Admin,SalesManager")] public async Task<IActionResult> SetTarget(TargetInput x, CancellationToken ct) { await targets.SetAsync(x, ct); return NoContent(); }

    /// <summary>Ranking by revenue after promotion – only orders closed by that Sales.</summary>
    [HttpGet("rankings/sales")] public Task<IReadOnlyList<SalesRankingDto>> SalesRanking([FromQuery] string? period, CancellationToken ct) => query.SalesRankingAsync(period, ct);
    [HttpGet("rankings/products")] public Task<IReadOnlyList<ProductRankingDto>> ProductRanking([FromQuery] string? period, [FromQuery] int top = 10, CancellationToken ct = default) => query.ProductRankingAsync(period, top, ct);

    [HttpGet("dashboard"), Authorize(Roles = "Admin,SalesManager,Operator")] public Task<DashboardDto> Dashboard([FromQuery] string? period, CancellationToken ct) => query.DashboardAsync(period, ct);
}
