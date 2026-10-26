using System.Security.Claims;
using FSCM.API.Contracts.WarehouseConfiguration;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.Admin)]
[Route("api/v1/mlor-configurations")]
public sealed class MlorConfigurationsController(WarehouseConfigurationService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<MlorResult>> List(
        [FromQuery] Guid? retailerId, [FromQuery] Guid? productCategoryId, [FromQuery] Guid? productId,
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        service.ListMlorAsync(retailerId, productCategoryId, productId, includeInactive, cancellationToken);

    [HttpPost]
    public Task<MlorResult> Set(SetMlorRequest request, CancellationToken cancellationToken)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
            throw WarehouseConfigurationOperationException.Validation("Authenticated user identifier is invalid.");
        return service.SetMlorAsync(request.RetailerId, request.ProductCategoryId, request.ProductId,
            request.MinimumLifeRatio, request.MinimumLifeHours, userId, cancellationToken);
    }

    [HttpDelete("{mlorId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid mlorId, CancellationToken cancellationToken)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
            throw WarehouseConfigurationOperationException.Validation("Authenticated user identifier is invalid.");
        await service.DeactivateMlorAsync(mlorId, userId, cancellationToken);
        return NoContent();
    }
}
