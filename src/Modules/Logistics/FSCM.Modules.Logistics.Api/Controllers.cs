using FSCM.Modules.Logistics.Application;
using FSCM.Modules.Logistics.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Logistics.Api;

[ApiController, Route("api/v1/transit-table"), Authorize(Roles = "Admin")]
public sealed class TransitTableController(TransitTableService svc) : ControllerBase
{
    [HttpGet] public Task<TransitTableDto> Get(CancellationToken ct) => svc.GetAsync(ct);
    [HttpPut] public async Task<IActionResult> Put(TransitTableDto input, CancellationToken ct) { await svc.UpdateAsync(input, ct); return NoContent(); }
}

/// <summary>Distance/transit lookup used by the portal when previewing an order or a transfer.</summary>
[ApiController, Route("api/v1/transit"), Authorize]
public sealed class TransitQuoteController(ILogisticsModule logistics) : ControllerBase
{
    [HttpGet("quote")]
    public async Task<ActionResult<TransitQuoteDto>> Quote([FromQuery] string from, [FromQuery] string to, [FromQuery] Purpose purpose = Purpose.WarehouseToRetailer, CancellationToken ct = default)
    {
        var q = await logistics.QuoteAsync(from, to, purpose, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), ct);
        return q is null ? NotFound(new ProblemDetails { Title = "Ngoài phạm vi phục vụ hoặc chưa có khoảng cách.", Status = 404 }) : q;
    }
}

[ApiController, Route("api/v1/vehicles"), Authorize(Roles = "Admin,Operator")]
public sealed class VehiclesController(FleetService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<VehicleDto>> List([FromQuery] bool activeOnly, CancellationToken ct) => svc.VehiclesAsync(activeOnly, ct);
    [HttpPost] public async Task<IActionResult> Add(VehicleDto x, CancellationToken ct) => Ok(new { plate = await svc.AddVehicleAsync(x, ct) });
    [HttpPut("{plate}")] public async Task<IActionResult> Update(string plate, VehicleDto x, CancellationToken ct) { await svc.UpdateVehicleAsync(plate, x, ct); return NoContent(); }
}

[ApiController, Route("api/v1/drivers"), Authorize(Roles = "Admin,Operator")]
public sealed class DriversController(FleetService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<DriverDto>> List([FromQuery] bool activeOnly, CancellationToken ct) => svc.DriversAsync(activeOnly, ct);
    [HttpPost] public async Task<IActionResult> Add(DriverDto x, CancellationToken ct) => Ok(new { code = await svc.AddDriverAsync(x, ct) });
    [HttpPut("{code}")] public async Task<IActionResult> Update(string code, DriverDto x, CancellationToken ct) { await svc.UpdateDriverAsync(code, x, ct); return NoContent(); }
}
