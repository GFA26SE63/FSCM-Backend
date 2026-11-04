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
    public static readonly string[] All = [PendingRefund, AwaitingVerification, EvidenceRejected, Refunded];
}

public sealed record CreateRefundData(Guid SalesOrderId, decimal Amount, string Recipient, string Reason);
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
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
