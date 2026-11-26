using System.Security.Claims;
using FSCM.Domain.Authorization;
using FSCM.Persistence.WarehouseWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Route("api/v1/transport-dispatches")]
[Authorize]
public sealed class TransportDispatchesController(WarehouseWorkService service) : ControllerBase
{
    public sealed record AssignTransferRequest(Guid DriverId, Guid VehicleId, int ExpectedVersion);

    [HttpPost("transfers/{id:guid}/assignment")]
    public async Task<IActionResult> AssignTransfer(Guid id, AssignTransferRequest request, CancellationToken ct)
    {
        var actor = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        await service.AssignTransferTransportAsync(id, request.DriverId, request.VehicleId,
            request.ExpectedVersion, actor, role, ct);
        return NoContent();
    }
}
