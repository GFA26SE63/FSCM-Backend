using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Refunds;

public sealed record RefundView(
    ManualRefund Refund, Payment Payment, SalesOrder Order, Retailer Retailer,
    decimal PendingRefundAmount, decimal RefundedAmount);

public interface IRefundStore
{
    Task<RefundView?> FindAsync(Guid id, bool forUpdate, CancellationToken cancellationToken);
    Task<RefundView?> FindByOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RefundView>> ListAsync(Guid? orderId, string? status,
        Guid actorUserId, string actorRole, CancellationToken cancellationToken);
    Task<(decimal Pending, decimal Refunded)> TotalsAsync(Guid paymentId, CancellationToken cancellationToken);
    void Add(ManualRefund refund);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
