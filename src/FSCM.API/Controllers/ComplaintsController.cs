using System.Security.Claims;
using FSCM.API.Contracts.Complaints;
using FSCM.API.Infrastructure;
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
    [HttpGet("orders/{orderId:guid}/targets")]
    public Task<IReadOnlyCollection<ComplaintTarget>> Targets(Guid orderId, CancellationToken ct) =>
        service.ListTargetsAsync(ActorId(), orderId, ct);

    [Authorize(Roles = SystemRoles.Retailer)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateComplaintRequest request, CancellationToken ct)
    {
        var created = await service.CreateAsync(ActorId(), new(request.OrderCode, request.Sku, request.LabelCode,
            request.ActualQty, request.Note, request.EvidenceUrl, request.ShipmentId,
            request.AllocationPlanPriceId, request.AllocationId, request.IssueType), ct);
        var result = (await service.ListAsync(ActorId(), SystemRoles.Retailer, ct)).Single(x => x.Id == created.Id);
        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id:guid}/resolve")]
    public Task<ComplaintResult> Resolve(Guid id, ResolveComplaintRequest request, CancellationToken ct) =>
        service.ResolveAsync(id, ActorId(), new(request.Result, request.Approved), ct);

    /// <summary>Retailer bổ sung ảnh bằng chứng cho khiếu nại của mình (nhiều ảnh, kể cả khi đang xem xét).</summary>
    [Authorize(Roles = SystemRoles.Retailer)]
    [HttpPost("{id:guid}/evidence")]
    [RequestSizeLimit(ImageUploadLimits.MaxRequestBytes)]
    [ProducesResponseType<ComplaintEvidenceResult>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ComplaintEvidenceResult>> AddEvidence(
        Guid id, IFormFile file, CancellationToken ct)
    {
        await using var upload = await file.ToImageUploadAsync(ct);
        var result = await service.AddEvidenceAsync(id, ActorId(), upload, ct);
        return CreatedAtAction(nameof(ListEvidence), new { id }, result);
    }

    [HttpGet("{id:guid}/evidence")]
    [ProducesResponseType<IReadOnlyCollection<ComplaintEvidenceResult>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<ComplaintEvidenceResult>> ListEvidence(Guid id, CancellationToken ct) =>
        service.ListEvidenceAsync(id, ActorId(), Role(), ct);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw ComplaintOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
}
