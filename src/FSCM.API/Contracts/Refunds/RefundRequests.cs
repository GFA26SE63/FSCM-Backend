namespace FSCM.API.Contracts.Refunds;

public sealed record CreateRefundRequest(Guid SalesOrderId, decimal Amount, string Recipient, string Reason);
public sealed record SubmitRefundEvidenceRequest(
    int ExpectedVersion, DateTimeOffset TransferredAt, string TransactionReference, string EvidenceReference);
public sealed record VerifyRefundRequest(int ExpectedVersion, string? Note = null);
