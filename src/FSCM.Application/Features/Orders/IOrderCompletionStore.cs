namespace FSCM.Application.Features.Orders;

/// <summary>The three facts that decide whether an order is settled, read in one pass.</summary>
public sealed record OrderCompletionView(
    Guid SalesOrderId, string Status, bool PaymentSettled, bool HasOpenComplaint);

public interface IOrderCompletionStore
{
    Task<OrderCompletionView?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the order to Completed and records why, in one transaction.
    ///
    /// The history row is not decoration: Completed is the moment points are earned and a credit
    /// term stops running, so afterwards somebody will want to know exactly when it happened and
    /// what made it true.
    /// </summary>
    Task CompleteAsync(
        Guid salesOrderId, Guid? actorUserId, DateTimeOffset now, CancellationToken cancellationToken);
}
