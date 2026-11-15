using System.Security.Claims;
using FSCM.API.Contracts.Refunds;
using FSCM.API.Infrastructure;
using FSCM.Application.Features.Refunds;
using FSCM.Application.Features.Storage;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales},{SystemRoles.Retailer}")]
[Route("api/v1/refunds")]
public sealed class RefundsController(RefundService service, ImageUploadService images) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<RefundResult>> List([FromQuery] Guid? orderId, [FromQuery] string? status,
        CancellationToken ct) => service.ListAsync(orderId, status, UserId(), Role(), ct);

    /// <summary>S3-101-4: payments owed a refund that has not been raised as a refund record yet.</summary>
    [HttpGet("obligations")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public Task<IReadOnlyCollection<RefundObligationResult>> Obligations(CancellationToken ct) =>
        service.ListObligationsAsync(Role(), ct);

    [HttpGet("{id:guid}")]
    public Task<RefundResult> Get(Guid id, CancellationToken ct) => service.GetAsync(id, UserId(), Role(), ct);

    [HttpPost]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public async Task<IActionResult> Create(CreateRefundRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(new(request.SalesOrderId, request.Amount, request.Recipient, request.Reason, request.ReceiptDiscrepancyId),
            Request.Headers["Idempotency-Key"].ToString(), UserId(), Role(), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/evidence")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public Task<RefundResult> Evidence(Guid id, SubmitRefundEvidenceRequest request, CancellationToken ct) =>
        service.SubmitEvidenceAsync(id, new(request.ExpectedVersion, request.TransferredAt,
            request.TransactionReference, request.EvidenceReference), Request.Headers["Idempotency-Key"].ToString(), UserId(), Role(), ct);

    [HttpPost("{id:guid}/verify")]
    [Authorize(Roles = SystemRoles.Admin)]
    public Task<RefundResult> Verify(Guid id, VerifyRefundRequest request, CancellationToken ct) =>
        service.VerifyAsync(id, new(request.ExpectedVersion, request.Approved, request.Note),
            Request.Headers["Idempotency-Key"].ToString(), UserId(), Role(), ct);

    /// <summary>F101: uploads one transfer-proof image; the returned reference goes into the evidence request.</summary>
    [HttpPost("evidence-files")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [RequestSizeLimit(ImageUploadLimits.MaxRequestBytes)]
    public async Task<RefundEvidenceFileResult> UploadEvidenceFile(IFormFile file, CancellationToken ct)
    {
        await using var upload = await file.ToImageUploadAsync(ct);
        var stored = await images.ValidateAndUploadAsync(EvidenceCategory, upload, ct);
        return new(EvidencePrefix + stored.BlobKey, stored.FileName, images.GetReadUrl(EvidenceCategory, stored.BlobKey));
    }

    /// <summary>F101: short-lived read URL for an uploaded proof image (Admin/Operator only).</summary>
    [HttpGet("evidence-files/url")]
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    public ActionResult<RefundEvidenceUrlResult> EvidenceUrl([FromQuery] string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith(EvidencePrefix, StringComparison.Ordinal))
            return new RefundEvidenceUrlResult(null);
        var key = reference[EvidencePrefix.Length..];
        if (key.Contains("..") || !key.StartsWith("claim/", StringComparison.Ordinal))
            throw RefundOperationException.Validation("Evidence reference is invalid.");
        return new RefundEvidenceUrlResult(images.GetReadUrl(EvidenceCategory, key));
    }

    private const string EvidencePrefix = "blob:";
    private const ImageCategory EvidenceCategory = ImageCategory.ClaimEvidence;
    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw RefundOperationException.Validation("Authenticated user identifier is invalid.");
    private string Role() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw RefundOperationException.Validation("Authenticated user role is invalid.");
}

public sealed record RefundEvidenceFileResult(string Reference, string FileName, string ReadUrl);
public sealed record RefundEvidenceUrlResult(string? Url);
