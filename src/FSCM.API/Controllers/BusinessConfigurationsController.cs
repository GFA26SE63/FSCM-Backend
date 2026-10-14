using System.Security.Claims;
using FSCM.API.Contracts.WarehouseConfiguration;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/configurations")]
public sealed class BusinessConfigurationsController(WarehouseConfigurationService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<ConfigurationResult>> List(CancellationToken cancellationToken) =>
        service.ListCurrentConfigurationsAsync(cancellationToken);

    [HttpGet("{key}/history")]
    public Task<IReadOnlyCollection<ConfigurationResult>> History(string key, CancellationToken cancellationToken) =>
        service.ListConfigurationHistoryAsync(key, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{key}")]
    public Task<ConfigurationResult> Update(
        string key, UpdateBusinessConfigurationRequest request, CancellationToken cancellationToken)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
            throw WarehouseConfigurationOperationException.Validation("Authenticated user identifier is invalid.");
        return service.UpdateConfigurationAsync(key, request.Value.GetRawText(), userId, cancellationToken);
    }
}
