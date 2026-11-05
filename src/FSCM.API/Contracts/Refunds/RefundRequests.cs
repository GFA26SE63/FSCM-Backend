namespace FSCM.API.Contracts.Refunds;

public sealed record CreateRefundRequest(
    Guid SalesOrderId, decimal Amount, string Recipient, string Reason, Guid? ReceiptDiscrepancyId = null);
public sealed record SubmitRefundEvidenceRequest(
    int ExpectedVersion, DateTimeOffset TransferredAt, string TransactionReference, string EvidenceReference);
public sealed record VerifyRefundRequest(int ExpectedVersion, bool Approved = true, string? Note = null);
