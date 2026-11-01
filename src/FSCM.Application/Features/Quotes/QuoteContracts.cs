using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Quotes;

public static class SalesOrderQuoteStatuses
{
    public const string Issued = "Issued";
    public const string PendingRetailerAcceptance = Issued;
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
    public static readonly string[] All = [PendingRetailerAcceptance, Accepted, Rejected, Expired];
}

public static class PurchaseOrderStatuses
{
    public const string Issued = "Issued";
}

public sealed record SalesOrderQuoteResult(
    Guid Id, Guid SalesOrderId, string OrderCode, Guid AllocationRunId,
    int RevisionNumber, string Status, decimal TotalAmount, DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, string? RejectionReason,
    Guid? PurchaseOrderId, string? PurchaseOrderCode, int Version);

public sealed record AcceptQuoteRequestData(int ExpectedVersion);
public sealed record RejectQuoteRequestData(int ExpectedVersion, string Reason);
