using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed record PaymentView(Payment Payment, SalesOrder Order, Retailer Retailer);

public interface IPaymentPlanService
{
    Task CreateForDeliveredOrderAsync(
        SalesOrder order,
        Guid receiptActorUserId,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken);
}

public interface IPaymentStore
{
    Task<PaymentView?> FindByOrderAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken);
    Task<PaymentView?> FindByOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PaymentView>> ListAsync(
        Guid actorUserId, string actorRole, CancellationToken cancellationToken);
    Task<bool> CanViewAsync(
        PaymentView view, Guid actorUserId, string actorRole, CancellationToken cancellationToken);
    void Add(Payment payment);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
