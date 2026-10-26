using System.Security.Claims;
using FSCM.API.Contracts.Complaints;
using FSCM.Application.Features.Complaints;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.Retailer}")]
[Route("api/v1/complaints")]
public sealed class ComplaintsController(ComplaintService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<ComplaintResult>> List(CancellationToken ct) => service.ListAsync(ActorId(), Role(), ct);

    [Authorize(Roles = SystemRoles.Retailer)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateComplaintRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(ActorId(), new(request.OrderCode, request.Sku, request.LabelCode,
            request.ActualQty, request.Note, request.EvidenceUrl), ct);
        var result = (await service.ListAsync(ActorId(), SystemRoles.Retailer, ct)).Single(x => x.Id == created.Id);
        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id:guid}/resolve")]
    public Task<ComplaintResult> Resolve(Guid id, ResolveComplaintRequest request, CancellationToken ct) =>
        service.ResolveAsync(id, ActorId(), new(request.Result), ct);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw ComplaintOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
}
