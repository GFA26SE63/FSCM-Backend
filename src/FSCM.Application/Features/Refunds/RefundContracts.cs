namespace FSCM.Application.Features.Refunds;

public static class RefundStatuses
{
    public const string PendingRefund = "PendingRefund";
    public const string AwaitingVerification = "AwaitingVerification";

    /// <summary>
    /// F5 step 18: Admin rejected the submitted transfer evidence. The refund obligation stands and
    /// the prior evidence is kept for history; the Operator resubmits corrected evidence from here.
    /// </summary>
    public const string EvidenceRejected = "EvidenceRejected";
    public const string Refunded = "Refunded";
    /// <summary>Statuses where the refund obligation is still open (not yet transferred and verified).</summary>
    public static readonly string[] Open = [PendingRefund, AwaitingVerification, EvidenceRejected];
    public static readonly string[] All = [PendingRefund, AwaitingVerification, EvidenceRejected, Refunded];
}

public sealed record CreateRefundData(
    Guid SalesOrderId, decimal Amount, string Recipient, string Reason, Guid? ReceiptDiscrepancyId = null);

/// <summary>S3-101-2: what a claim authorizes to be refunded, and how much is already requested.</summary>
public sealed record RefundClaimSource(
    Guid ClaimId, Guid SalesOrderId, bool IsDecided, string? ResolutionType,
    decimal AdjustmentAmount, decimal AlreadyLinkedAmount);

/// <summary>
/// S3-101-4: a payment that has collected more than it is now owed (after an approved claim) and
/// still has an unrequested refund amount, so an Operator knows where to raise a refund record.
/// </summary>
public sealed record RefundObligationResult(
    Guid PaymentId, Guid SalesOrderId, string OrderCode, string RetailerName,
    decimal ConfirmedAmount, decimal AdjustedAmount, decimal RefundedAmount,
    decimal PendingRefundAmount, decimal UnrequestedAmount,
    IReadOnlyCollection<Guid> ClaimIds);
public sealed record SubmitRefundEvidenceData(
    int ExpectedVersion, DateTimeOffset TransferredAt, string TransactionReference, string EvidenceReference);
public sealed record VerifyRefundData(int ExpectedVersion, bool Approved, string? Note);

public sealed record RefundResult(
    Guid Id, Guid PaymentId, Guid SalesOrderId, string OrderCode, decimal Amount,
    string Recipient, string Reason, string Status, DateTimeOffset? TransferredAt,
    string? TransactionReference, string? EvidenceReference, Guid CreatedByUserId,
    Guid? SubmittedByUserId, DateTimeOffset? SubmittedAt, Guid? VerifiedByUserId,
    DateTimeOffset? VerifiedAt, string? VerificationNote, int Version,
    decimal FinalApprovedAmount, decimal AdjustedAmount, decimal ConfirmedAmount,
    decimal PendingRefundAmount, decimal RefundedAmount, decimal RefundDueAmount,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    /// <summary>The claim this refund resolves, when it came from an approved financial adjustment.</summary>
    public Guid? ReceiptDiscrepancyId { get; init; }
}
