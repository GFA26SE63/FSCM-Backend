namespace FSCM.Application.Features.Payments;

public sealed record RetailerReceivableFilter(
    string? Status,
    string? OrderCode,
    DateOnly? DueFrom,
    DateOnly? DueTo,
    bool? Overdue,
    int Page = 1,
    int PageSize = 20);

public sealed record ReceivableCollectionHistory(
    Guid Id,
    string Method,
    decimal Amount,
    string Status,
    DateTimeOffset? ConfirmedAt,
    string? Reference,
    string? Note);

public sealed record ReceivableDisputeHistory(
    Guid Id,
    string Status,
    decimal DisputedAmount,
    DateTimeOffset ReportedAt,
    DateTimeOffset? ResolvedAt,
    string? Note);

public sealed record ReceivableRefundHistory(
    Guid Id,
    decimal Amount,
    string Status,
    DateTimeOffset? TransferredAt,
    string? TransactionReference,
    DateTimeOffset? VerifiedAt,
    string Reason);

public sealed record RetailerReceivableView(
    PaymentView Payment,
    IReadOnlyCollection<ReceivableCollectionHistory> Collections,
    IReadOnlyCollection<ReceivableDisputeHistory> Disputes,
    IReadOnlyCollection<ReceivableRefundHistory> Refunds);

public sealed record RetailerReceivableResult(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    string PaymentMethod,
    string Status,
    decimal OriginalOrderAmount,
    decimal FinalApprovedAmount,
    decimal ApprovedAdjustmentAmount,
    decimal AdjustedAmount,
    decimal ConfirmedCollectedAmount,
    decimal OutstandingAmount,
    decimal PendingDisputedAmount,
    decimal CollectibleAmount,
    DateOnly? DueDate,
    bool IsOverdue,
    int DaysOverdue,
    decimal PendingRefundAmount,
    decimal CompletedRefundAmount,
    decimal RefundDueAmount,
    IReadOnlyCollection<ReceivableCollectionHistory> Collections,
    IReadOnlyCollection<ReceivableDisputeHistory> Disputes,
    IReadOnlyCollection<ReceivableRefundHistory> Refunds,
    DateTimeOffset UpdatedAt);

public sealed record RetailerReceivablePage(
    IReadOnlyCollection<RetailerReceivableResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public interface IRetailerReceivableStore
{
    Task<IReadOnlyCollection<RetailerReceivableView>> ListAsync(
        Guid retailerUserId, CancellationToken cancellationToken);

    Task<RetailerReceivableView?> FindByOrderAsync(
        Guid retailerUserId, Guid orderId, CancellationToken cancellationToken);
}
