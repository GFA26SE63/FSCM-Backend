using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed record PaymentView(Payment Payment, string OrderCode);

public interface IPaymentStore
{
    Task<SalesOrder?> FindOrderAsync(Guid salesOrderId, CancellationToken cancellationToken);
    Task<Payment?> FindPaymentForUpdateAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<PaymentView?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PaymentView>> ListByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken);
    Task<decimal> SumConfirmedAmountAsync(Guid salesOrderId, CancellationToken cancellationToken);

    /// <summary>True when the actor may view/record payments for the given order under its role scope.</summary>
    Task<bool> CanAccessOrderAsync(Guid userId, string role, SalesOrder order, CancellationToken cancellationToken);

    void AddPayment(Payment payment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
