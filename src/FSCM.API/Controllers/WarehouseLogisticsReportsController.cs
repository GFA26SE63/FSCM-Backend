using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

/// <summary>"Báo cáo kho vận": fleet size, current stock buckets and period movements of the best-selling SKUs per warehouse.</summary>
[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
[Route("api/v1/reports/warehouse-logistics")]
public sealed class WarehouseLogisticsReportsController(IWarehouseLogisticsReportStore store, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public Task<WarehouseLogisticsReport> Read(
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        [FromQuery] Guid? regionId,
        [FromQuery] int top = WarehouseLogisticsReportQuery.DefaultTop,
        // Comma-separated warehouse ids to include; empty = all active warehouses in the region.
        [FromQuery] string? warehouseIds = null,
        CancellationToken ct = default)
    {
        var ids = new List<Guid>();
        foreach (var part in (warehouseIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            ids.Add(Guid.TryParse(part, out var id) ? id : throw ReportOperationException.Validation("Warehouse id is invalid."));
        var query = new WarehouseLogisticsReportQuery(fromDate, toDate, regionId, top, ids.Distinct().ToArray());
        WarehouseLogisticsReportBuilder.Validate(query);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
        return store.ReadAsync(query, today, ct);
    }
}
