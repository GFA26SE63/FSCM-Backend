// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class ManualRefund
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid SalesOrderId { get; set; }
    public decimal Amount { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreateOperationKey { get; set; } = string.Empty;
    public string CreateRequestHash { get; set; } = string.Empty;
    public string? EvidenceOperationKey { get; set; }
    public string? EvidenceRequestHash { get; set; }
    public DateTimeOffset? TransferredAt { get; set; }
    public string? TransactionReference { get; set; }
    public string? EvidenceReference { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? VerificationOperationKey { get; set; }
    public string? VerificationRequestHash { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
    public Guid CreatedByUserId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
