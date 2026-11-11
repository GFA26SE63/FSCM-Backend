using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Notifications;
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
    OrderNotificationService? notifications = null)
{
    private const ImageCategory Category = ImageCategory.ClaimEvidence;

    // Complaint is still open for more evidence until it is decided/resolved.
    private static readonly string[] ClosedStatuses = ["Approved", "Rejected", "Resolved", "Cancelled"];
    public Task<ReceiptDiscrepancy> CreateAsync(Guid actor, CreateComplaintData data, CancellationToken ct) =>
        store.CreateAsync(actor, data, timeProvider.GetUtcNow(), ct);
    public Task<IReadOnlyCollection<ComplaintResult>> ListAsync(Guid actor, string role, CancellationToken ct) =>
        store.ListAsync(actor, role, ct);
    public Task<IReadOnlyCollection<ComplaintTarget>> ListTargetsAsync(Guid actor, Guid orderId, CancellationToken ct) =>
        store.ListTargetsAsync(actor, orderId, ct);
    public async Task<ComplaintResult> ResolveAsync(Guid id, Guid actor, ResolveComplaintData data, CancellationToken ct)
    {
        ComplaintResult? result = null;
        await store.ExecuteInTransactionAsync(async token => result = await ResolveCoreAsync(id, actor, data, token), ct);
        return result!;
    }

    private async Task<ComplaintResult> ResolveCoreAsync(
        Guid id, Guid actor, ResolveComplaintData data, CancellationToken ct)
    {
        var resolved = await store.ResolveAsync(id, actor, data, timeProvider.GetUtcNow(), ct);
        if (resolved.Status == "Approved" && !resolved.FinancialAdjustmentApplied &&
            resolved.AdjustmentAmount > 0 && payments is not null)
        {
            var payment = await payments.GetByOrderAsync(
                resolved.SalesOrderId, actor, SystemRoles.Operator, ct);
            var reason = $"Khiếu nại {resolved.Code} được chấp nhận: {resolved.Response?.Trim()}";
            await payments.AdjustReceivableAsync(
                resolved.SalesOrderId,
                new AdjustReceivableData(
                    resolved.AdjustmentAmount,
                    reason,
                    payment.Version),
                $"complaint-adjust:{resolved.Id:N}",
                actor,
                SystemRoles.Operator,
                ct);
            await store.MarkFinancialAdjustmentAppliedAsync(resolved.Id, ct);
            resolved = resolved with { FinancialAdjustmentApplied = true };
        }
        // An order held back only by this complaint can now earn its points, and nothing else is
        // going to ask again on its behalf.
        await completion.EvaluateAsync(resolved.SalesOrderId, actor, ct);
        if (notifications is not null)
        {
            await notifications.PublishRetailerAsync(resolved.SalesOrderId,
                resolved.Status == "Approved" ? "ComplaintApproved" : "ComplaintRejected",
                resolved.Status == "Approved" ? "Khiếu nại đã được chấp nhận" : "Khiếu nại đã bị từ chối",
                resolved.Status == "Approved"
                    ? $"Khiếu nại {resolved.Code} đã được duyệt. Nghĩa vụ thanh toán được điều chỉnh và khoản đã thu thừa (nếu có) sẽ được hoàn lại."
                    : $"Khiếu nại {resolved.Code} đã bị từ chối. Xem kết quả xử lý để biết lý do.",
                resolved.Id, "/retailer/complaints", ct);
        }
        return resolved;
    }

    /// <summary>Retailer withdraws a claim that has not been decided yet.</summary>
    public async Task<ComplaintResult> CancelAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await store.ExecuteInTransactionAsync(async token =>
        {
            await store.CancelAsync(id, actor, timeProvider.GetUtcNow(), token);
            // A cancelled claim no longer holds the order open.
            var claim = (await store.ListAsync(actor, SystemRoles.Retailer, token)).FirstOrDefault(x => x.Id == id);
            if (claim is not null) await completion.EvaluateAsync(claim.SalesOrderId, actor, token);
        }, ct);
        return (await store.ListAsync(actor, SystemRoles.Retailer, ct)).First(x => x.Id == id);
    }

    public async Task<ComplaintResult> RequestEvidenceAsync(Guid id, Guid actor, string reason, CancellationToken ct)
    {
        await store.RequestEvidenceAsync(id, actor, reason, timeProvider.GetUtcNow(), ct);
        var result = (await store.ListAsync(actor, SystemRoles.Admin, ct)).FirstOrDefault(x => x.Id == id)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");
        if (notifications is not null)
            await notifications.PublishRetailerAsync(result.SalesOrderId, "ComplaintEvidenceRequested",
                "Khiếu nại cần bổ sung bằng chứng", $"Khiếu nại {result.Code} cần bổ sung bằng chứng: {reason.Trim()}",
                result.Id, "/retailer/complaints", ct);
        return result;
    }

    public async Task<ComplaintResult> GetAsync(Guid id, Guid actor, string role, CancellationToken ct) =>
        (await store.ListAsync(actor, role, ct)).FirstOrDefault(x => x.Id == id)
            ?? throw ComplaintOperationException.NotFound("Complaint was not found.");

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

    /// <summary>
    /// Retailer removes one evidence image of their own claim while it has no decision yet, so they
    /// can pick a different one. Stored files are deleted after the record is gone.
    /// </summary>
    public async Task RemoveEvidenceAsync(Guid complaintId, Guid evidenceId, Guid actor, CancellationToken ct)
    {
        var complaint = await store.FindOwnedComplaintAsync(complaintId, actor, ct)
            ?? throw ImageStorageException.NotFound("Không tìm thấy khiếu nại của bạn.");
        if (ClosedStatuses.Contains(complaint.Status))
            throw ImageStorageException.Validation("Khiếu nại đã có kết quả, không thể xóa ảnh.");
        var blobKey = await store.RemoveEvidenceAsync(complaintId, evidenceId, actor, timeProvider.GetUtcNow(), ct)
            ?? throw ImageStorageException.NotFound("Không tìm thấy ảnh bằng chứng.");
        try { await images.DeleteAsync(Category, blobKey, ct); }
        catch (Exception) { /* The record is gone; an orphaned blob is harmless and can be cleaned up later. */ }
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
