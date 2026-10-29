using FSCM.Application.Features.Orders;

namespace FSCM.UnitTests.Orders;

/// <summary>
/// In-memory order state for the completion tests, and a no-op for the services that merely have
/// to hand one to <see cref="OrderCompletionService"/>.
///
/// With no order registered, <see cref="FindAsync"/> answers null and nothing happens at all -
/// which is what a shipment or payment test wants, since neither is about completion.
/// </summary>
public sealed class FakeOrderCompletionStore : IOrderCompletionStore
{
    public Dictionary<Guid, OrderCompletionView> Views { get; } = [];

    public List<Guid> Completed { get; } = [];

    /// <summary>
    /// Run when an order is completed, so a test can move whatever else sees that order. In
    /// production the status is committed before the points are counted; a fake that skipped that
    /// would let the ledger read a status the real one never would.
    /// </summary>
    public Action<Guid>? OnCompleted { get; set; }

    public Task<OrderCompletionView?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(Views.GetValueOrDefault(salesOrderId));

    public Task CompleteAsync(
        Guid salesOrderId, Guid? actorUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Completed.Add(salesOrderId);
        if (Views.TryGetValue(salesOrderId, out var view))
        {
            Views[salesOrderId] = view with { Status = OrderStatuses.Completed };
        }

        OnCompleted?.Invoke(salesOrderId);
        return Task.CompletedTask;
    }
}
