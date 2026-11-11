using System.Security.Claims;
using FSCM.Application.Features.Dashboard;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper},{SystemRoles.SalesManager}")]
[Route("api/v1/dashboard")]
public sealed class DashboardController(DashboardService service) : ControllerBase
{
    [HttpGet]
    public Task<DashboardResult> Get([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? regionId, [FromQuery] Guid? warehouseId, [FromQuery] Guid? salesGroupId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            throw ReportOperationException.Forbidden("Phiên đăng nhập không hợp lệ.");
        return service.GetAsync(actor, User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            new(fromDate, toDate, regionId, warehouseId, salesGroupId), ct);
    }
}
