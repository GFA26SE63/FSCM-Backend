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
    /// <summary>
    /// The run-day timetable: when Sales are advised to have sent, when the cutoff closes the run,
    /// and when a decision on the proposal is expected. Nothing on it approves anything by itself.
    /// </summary>
    [HttpGet("schedule")]
    public Task<AllocationScheduleResult> Schedule(
        [FromQuery] DateOnly? businessDate, CancellationToken cancellationToken) =>
        service.GetScheduleAsync(businessDate, cancellationToken);

    [HttpPost("estimate")]
    public Task<EtaResult> Estimate(EstimateEtaRequest request, CancellationToken cancellationToken) =>
        service.EstimateAsync(
            new EtaRequest(request.WarehouseId, request.RetailerId, request.ReferenceAt), cancellationToken);
}
