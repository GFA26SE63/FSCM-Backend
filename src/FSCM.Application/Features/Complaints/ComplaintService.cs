using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Notifications;
using FSCM.Application.Features.Refunds;
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
    OrderNotificationService? notifications = null,
    RefundService? refunds = null)
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
        if (data.Approved && data.SettlementMethod is "DebtOffset" or "CashRefund" && payments is not null)
        {
            var current = await GetAsync(id, actor, SystemRoles.Admin, ct);
            if (current.Status == "Open")
            {
                var payment = await payments.GetByOrderAsync(current.SalesOrderId, actor, SystemRoles.Operator, ct);
                var quantity = data.ApprovedQuantity ?? current.RequestedQuantity;
                if (quantity <= 0 || decimal.Truncate(quantity) != quantity || quantity > current.RequestedQuantity)
                    throw ComplaintOperationException.Validation("Số thùng được duyệt phải nguyên, lớn hơn 0 và không vượt số yêu cầu.");
                var amount = Math.Round(quantity * current.FinalUnitPrice, 0, MidpointRounding.AwayFromZero);
                if (amount > payment.AdjustedAmount) throw ComplaintOperationException.Conflict("Số tiền yêu cầu vượt nghĩa vụ còn được điều chỉnh của đơn.");
                var forecast = RequestSettlement.Calculate(amount, payment.AdjustedAmount, payment.ConfirmedAmount, payment.RefundedAmount);
                if (data.SettlementMethod == "CashRefund" && forecast.Refund == 0)
                    throw ComplaintOperationException.Validation("Chưa có tiền đã thu thừa để hoàn; hãy chọn cấn trừ công nợ.");
                if (data.SettlementMethod == "DebtOffset" && forecast.DebtOffset == 0)
                    throw ComplaintOperationException.Validation("Đơn đã thanh toán đủ; hãy chọn hoàn tiền.");
            }
        }
        var resolved = await store.ResolveAsync(id, actor, data, timeProvider.GetUtcNow(), ct);
        resolved = await SettleIfReadyAsync(resolved, actor, ct);
        // An order held back only by this complaint can now earn its points, and nothing else is
        // going to ask again on its behalf.
        await completion.EvaluateAsync(resolved.SalesOrderId, actor, ct);
        if (notifications is not null)
        {
            await notifications.PublishRetailerAsync(resolved.SalesOrderId,
                data.Approved ? "ComplaintApproved" : "ComplaintRejected",
                data.Approved ? "Yêu cầu đã được chấp nhận" : "Yêu cầu đã bị từ chối",
                data.Approved
                    ? resolved.Status == "Resolved"
                        ? $"Yêu cầu {resolved.Code} đã được chốt; xem phần cấn trừ công nợ và phiếu hoàn tiền (nếu có)."
                        : resolved.PhysicalHandlingCompleted
                            ? $"Yêu cầu {resolved.Code} đã xử lý hàng, đang chờ đơn giao đủ để chốt tài chính."
                            : $"Yêu cầu {resolved.Code} đã được duyệt, đang chờ xác nhận xử lý hàng thực tế."
                    : $"Khiếu nại {resolved.Code} đã bị từ chối. Xem kết quả xử lý để biết lý do.",
                resolved.Id, "/retailer/complaints", ct);
        }
        return resolved;
    }

    public async Task<ComplaintResult> CompleteHandlingAsync(Guid id, Guid actor, CompleteRequestHandlingData data, CancellationToken ct)
    {
        ComplaintResult? result = null;
        await store.ExecuteInTransactionAsync(async token => {
            var handled = await store.CompleteHandlingAsync(id, actor, data, timeProvider.GetUtcNow(), token);
            result = await SettleIfReadyAsync(handled, actor, token);
            await completion.EvaluateAsync(result.SalesOrderId, actor, token);
        }, ct);
        return result!;
    }

    private async Task<ComplaintResult> SettleIfReadyAsync(ComplaintResult claim, Guid actor, CancellationToken ct)
    {
        if (claim.Status != "Approved" || !claim.PhysicalHandlingCompleted || claim.FinancialAdjustmentApplied)
            return claim;
        decimal offset = 0, refundAmount = 0;
        Guid? refundId = null;
        if (claim.ApprovedSettlementMethod != "None" && claim.AdjustmentAmount > 0)
        {
            if (payments is null || refunds is null) throw ComplaintOperationException.Conflict("Dịch vụ đối soát tài chính chưa sẵn sàng.");
            var before = await payments.GetByOrderAsync(claim.SalesOrderId, actor, SystemRoles.Operator, ct);
            // A returned shipment part can be handled before the other parts finish delivery.
            // Keep its physical evidence committed; Admin can settle once the ledger is activated.
            if (!before.DeliveredDate.HasValue) return claim;
            if (claim.AdjustmentAmount > before.AdjustedAmount) throw ComplaintOperationException.Conflict("Nghĩa vụ đơn đã thay đổi; cần đối soát trước khi chốt yêu cầu.");
            var split = RequestSettlement.Calculate(claim.AdjustmentAmount, before.AdjustedAmount, before.ConfirmedAmount, before.RefundedAmount);
            await payments.AdjustReceivableAsync(claim.SalesOrderId,
                new(claim.AdjustmentAmount, $"Khiếu nại {claim.Code} được chấp nhận: {claim.Response?.Trim()}", before.Version),
                $"complaint-adjust:{claim.Id:N}", actor, SystemRoles.Operator, ct);
            refundAmount = split.Refund;
            offset = split.DebtOffset;
            await store.MarkFinancialAdjustmentAppliedAsync(claim.Id, ct);
            if (refundAmount > 0)
            {
                var refund = await refunds.CreateAsync(new(claim.SalesOrderId, refundAmount, claim.RetailerName,
                    $"Hoàn theo yêu cầu {claim.Code}", claim.Id), $"request-refund:{claim.Id:N}", actor, SystemRoles.Operator, ct);
                refundId = refund.Id;
            }
        }
        await store.RecordSettlementAsync(claim.Id, offset, refundAmount, refundId, timeProvider.GetUtcNow(), ct);
        return (await store.ListAsync(actor, SystemRoles.Admin, ct)).Single(x => x.Id == claim.Id);
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
        await store.ExecuteInTransactionAsync(token => store.RequestEvidenceAsync(id, actor, reason, timeProvider.GetUtcNow(), token), ct);
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
