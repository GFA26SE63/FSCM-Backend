using FSCM.API.Contracts.Maps;
using FSCM.Application.Features.Maps;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Admin)]
[Route("api/v1/eta")]
public sealed class EtaController(MapService service) : ControllerBase
{
    [HttpPost("estimate")]
    public Task<EtaEstimateResult> Estimate(EstimateEtaRequest request, CancellationToken cancellationToken) =>
        service.EstimateEtaAsync(new EtaEstimateInput(
            request.WarehouseId, request.RetailerId, request.DepartureAt, request.BufferMinutes), cancellationToken);
}
