using FSCM.Domain.Entities;

namespace FSCM.Application.Features.ManualPayments;

public sealed record ManualPaymentView(
    ManualPayment Payment,
    string OrderCode,
    decimal FinalApprovedAmount,
    decimal TotalConfirmedAmount);

public interface IManualPaymentStore
{
    Task<SalesOrder?> FindOrderAsync(Guid salesOrderId, CancellationToken cancellationToken);
    Task<ManualPayment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<ManualPaymentView?> FindAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<ManualPaymentView?> FindByOperationKeyAsync(string operationKey, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ManualPaymentView>> ListByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken);

    /// <summary>True when the actor may view/record manual payments for the given order under its role scope.</summary>
    Task<bool> CanAccessOrderAsync(Guid userId, string role, SalesOrder order, CancellationToken cancellationToken);

    void Add(ManualPayment payment);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
