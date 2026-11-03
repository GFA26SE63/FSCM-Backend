using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using FSCM.Application.Features.Storage;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed class ComplaintService(
    IComplaintStore store,
    OrderCompletionService completion,
    TimeProvider timeProvider,
    ImageUploadService images,
    PaymentService? payments = null,
    LoyaltyAccrualService? loyalty = null)
{
    private const ImageCategory Category = ImageCategory.ClaimEvidence;

    // Complaint is still open for more evidence until it is decided/resolved.
    private static readonly string[] ClosedStatuses = ["Approved", "Rejected", "Resolved"];
    public Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, CancellationToken ct) =>
        store.CreateAsync(actor, data, timeProvider.GetUtcNow(), ct);
    public Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actor, string role, CancellationToken ct) =>
        store.ListAsync(actor, role, ct);
    public async Task<ComplaintResult> ResolveAsync(
        Guid id, Guid actor, ResolveComplaintData data, CancellationToken ct)
    {
        var resolved = await store.ResolveAsync(id, actor, data, timeProvider.GetUtcNow(), ct);
        if (data.Approved && !resolved.FinancialAdjustmentApplied &&
            resolved.AdjustmentAmount > 0 && payments is not null)
        {
            var payment = await payments.GetByOrderAsync(
                resolved.SalesOrderId, actor, SystemRoles.Operator, ct);
            var reason = $"Khiếu nại {resolved.Code} được chấp nhận: {data.Result.Trim()}";
            var adjusted = await payments.AdjustReceivableAsync(
                resolved.SalesOrderId,
                new AdjustReceivableData(
                    resolved.AdjustmentAmount,
                    reason,
                    payment.Version),
                $"complaint-adjust:{resolved.Id:N}",
                actor,
                SystemRoles.Operator,
                ct);
            if (loyalty is not null && adjusted.AdjustedAmount != payment.AdjustedAmount)
            {
                await loyalty.AdjustAsync(
                    resolved.SalesOrderId, adjusted.AdjustedAmount, reason, actor, ct);
            }
            await store.MarkFinancialAdjustmentAppliedAsync(resolved.Id, ct);
        }
        // An order held back only by this complaint can now earn its points, and nothing else is
        // going to ask again on its behalf.
        await completion.EvaluateAsync(resolved.SalesOrderId, actor, ct);
        return resolved;
    }

    /// <summary>
    /// Retailer uploads one evidence image to a complaint they own, while it is still open. The
    /// upload only appends evidence; it never changes the complaint status or resolution.
    /// </summary>
    public async Task<ComplaintEvidenceResult> AddEvidenceAsync(
        Guid complaintId, Guid actor, ImageUpload upload, CancellationToken ct)
    {
        var complaint = await store.FindOwnedComplaintAsync(complaintId, actor, ct)
            ?? throw ImageStorageException.NotFound("Không tìm thấy khiếu nại của bạn.");
        if (ClosedStatuses.Contains(complaint.Status))
            throw ImageStorageException.Validation("Khiếu nại đã có kết quả, không thể bổ sung ảnh.");

        var stored = await images.ValidateAndUploadAsync(Category, upload, ct);
        var evidence = new ComplaintEvidence
        {
            Id = Guid.NewGuid(),
            ReceiptDiscrepancyId = complaint.Id,
            UploadedByUserId = actor,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            BlobKey = stored.BlobKey,
            UploadedAt = timeProvider.GetUtcNow()
        };
        await store.AddEvidenceAsync(evidence, ct);
        return ToResult(evidence);
    }

    public async Task<IReadOnlyCollection<ComplaintEvidenceResult>> ListEvidenceAsync(
        Guid complaintId, Guid actor, string role, CancellationToken ct)
    {
        // Admin/Operator can read any complaint's evidence; a Retailer only their own.
        if (role == FSCM.Domain.Authorization.SystemRoles.Retailer &&
            await store.FindOwnedComplaintAsync(complaintId, actor, ct) is null)
            throw ImageStorageException.NotFound("Không tìm thấy khiếu nại của bạn.");
        var items = await store.ListEvidenceAsync(complaintId, ct);
        return items.Select(ToResult).ToArray();
    }

    private ComplaintEvidenceResult ToResult(ComplaintEvidence e) => new(
        e.Id, e.FileName, e.ContentType, images.GetReadUrl(Category, e.BlobKey), e.UploadedAt);
}
