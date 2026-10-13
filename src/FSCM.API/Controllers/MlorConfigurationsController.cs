using FSCM.API.Contracts.WarehouseConfiguration;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/mlor-configurations")]
public sealed class MlorConfigurationsController(WarehouseConfigurationService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<MlorResult>> List(
        [FromQuery] Guid? retailerId, [FromQuery] Guid? productId,
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListMlorAsync(retailerId, productId, includeInactive, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    public Task<MlorResult> Set(SetMlorRequest request, CancellationToken cancellationToken) =>
        service.SetMlorAsync(request.RetailerId, request.ProductId,
            request.MinimumLifeRatio, request.MinimumLifeHours, cancellationToken);
}
