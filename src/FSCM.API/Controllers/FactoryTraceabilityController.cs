using System.Security.Claims;
using FSCM.Domain.Authorization;
using FSCM.Persistence.WarehouseWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Route("api/v1/factory-traceability")]
[Authorize]
public sealed class FactoryTraceabilityController(FactoryTraceabilityService service, IConfiguration configuration) : ControllerBase
{
    [HttpGet("application")]
    public object Application() => new { url = configuration["Traceability:PublicUrl"] ?? configuration["Traceability:BaseUrl"] ?? "http://localhost:5250/" };
    [HttpGet("trace")]
    public Task<FactoryTraceResult> Trace([FromQuery] string code, CancellationToken ct) => service.TraceAsync(code,
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), User.FindFirstValue(ClaimTypes.Role)!, ct);
}
