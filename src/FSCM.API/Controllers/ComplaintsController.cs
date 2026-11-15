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
            request.AllocationPlanPriceId, request.AllocationId, request.IssueType, request.RequestType,
            request.SettlementMethod, request.AffectedQuantity, request.RequiresCollection, request.RequestId), ct);
        var result = (await service.ListAsync(ActorId(), SystemRoles.Retailer, ct)).Single(x => x.Id == created.Id);
        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/resolve")]
    public Task<ComplaintResult> Resolve(Guid id, ResolveComplaintRequest request, CancellationToken ct) =>
        service.ResolveAsync(id, ActorId(), new(request.Result, request.Approved, request.ApprovedQuantity,
            request.SettlementMethod, request.ExpectedVersion, request.WaivePhysicalHandling, request.WaiverReason), ct);

    [HttpGet("{id:guid}")]
    public Task<ComplaintResult> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ActorId(), Role(), ct);

    /// <summary>Admin/Operator yêu cầu Retailer bổ sung bằng chứng; hồ sơ chuyển UnderReview, lịch sử giữ nguyên.</summary>
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPut("{id:guid}/request-evidence")]
    public Task<ComplaintResult> RequestEvidence(Guid id, RequestComplaintEvidenceRequest request, CancellationToken ct) =>
        service.RequestEvidenceAsync(id, ActorId(), request.Reason, ct);

    /// <summary>Admin từ chối khiếu nại, bắt buộc có lý do.</summary>
    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/reject")]
    public Task<ComplaintResult> Reject(Guid id, RejectComplaintRequest request, CancellationToken ct) =>
        service.ResolveAsync(id, ActorId(), new(request.Reason, false, ExpectedVersion: request.ExpectedVersion), ct);

    /// <summary>Admin duyệt khiếu nại; xử lý bằng tiền (điều chỉnh công nợ / hoàn tiền F101).</summary>
    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{id:guid}/approve")]
    public Task<ComplaintResult> Approve(Guid id, ApproveComplaintRequest request, CancellationToken ct) =>
        service.ResolveAsync(id, ActorId(),
            new(string.IsNullOrWhiteSpace(request.Note) ? "Approved" : request.Note, true, request.ApprovedQuantity,
                request.SettlementMethod, request.ExpectedVersion, request.WaivePhysicalHandling, request.WaiverReason), ct);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{id:guid}/complete-handling")]
    public Task<ComplaintResult> CompleteHandling(Guid id, CompleteRequestHandlingRequest request, CancellationToken ct) =>
        service.CompleteHandlingAsync(id, ActorId(), new(request.ExpectedVersion, request.ActualQuantity,
            request.CompletedAt, request.Note, request.EvidenceReference), ct);

    /// <summary>Retailer hủy khiếu nại của mình khi chưa được duyệt hoặc từ chối.</summary>
    [Authorize(Roles = SystemRoles.Retailer)]
    [HttpPost("{id:guid}/cancel")]
    public Task<ComplaintResult> Cancel(Guid id, CancellationToken ct) => service.CancelAsync(id, ActorId(), ct);

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

    /// <summary>Retailer xóa ảnh bằng chứng của khiếu nại chưa được xử lý để chọn lại.</summary>
    [Authorize(Roles = SystemRoles.Retailer)]
    [HttpDelete("{id:guid}/evidence/{evidenceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveEvidence(Guid id, Guid evidenceId, CancellationToken ct)
    {
        await service.RemoveEvidenceAsync(id, evidenceId, ActorId(), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/evidence")]
    [ProducesResponseType<IReadOnlyCollection<ComplaintEvidenceResult>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<ComplaintEvidenceResult>> ListEvidence(Guid id, CancellationToken ct) =>
        service.ListEvidenceAsync(id, ActorId(), Role(), ct);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw ComplaintOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
}
