using FSCM.API.Contracts.Maps;
using FSCM.Application.Features.DeliveryEta;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Admin)]
[Route("api/v1/eta")]
public sealed class EtaController(DeliveryEtaService service) : ControllerBase
{
    [HttpPost("estimate")]
    public Task<EtaResult> Estimate(EstimateEtaRequest request, CancellationToken cancellationToken) =>
        service.EstimateAsync(
            new EtaRequest(request.WarehouseId, request.RetailerId, request.ReferenceAt), cancellationToken);
}
