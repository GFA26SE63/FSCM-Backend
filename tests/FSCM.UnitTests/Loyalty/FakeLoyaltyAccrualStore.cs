using FSCM.Application.Features.Loyalty;

namespace FSCM.UnitTests.Loyalty;

/// <summary>
/// In-memory points ledger for the accrual tests, and a no-op for the services that merely have to
/// hand one to <see cref="LoyaltyAccrualService"/>.
///
/// With no view registered, <see cref="FindAsync"/> answers null and the service does nothing at
/// all - which is what a shipment or payment test wants, since neither is about loyalty.
/// </summary>
public sealed class FakeLoyaltyAccrualStore : ILoyaltyAccrualStore
{
    public Dictionary<Guid, LoyaltyAccrualView> Views { get; } = [];

    public List<(Guid Balance, Guid? Order, string Type, int Points, bool MovesBalance, string Note)> Rows { get; }
        = [];

    public Dictionary<Guid, int> Balances { get; } = [];

    public int SaveCount { get; private set; }

    public Task<LoyaltyAccrualView?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(Views.GetValueOrDefault(salesOrderId));

    public Task<RetailerBalanceSnapshot> EnsureBalanceAsync(
        Guid retailerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Balances.TryAdd(retailerId, 0);
        return Task.FromResult(new RetailerBalanceSnapshot(retailerId, Balances[retailerId], 0));
    }

    public Task<int> AppendAsync(
        Guid retailerLoyaltyId, Guid loyaltyProgramId, Guid? salesOrderId, Guid? actorUserId,
        string transactionType, int points, bool movesBalance, string note,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        Rows.Add((retailerLoyaltyId, salesOrderId, transactionType, points, movesBalance, note));
        Balances.TryAdd(retailerLoyaltyId, 0);
        if (movesBalance) Balances[retailerLoyaltyId] += points;
        return Task.FromResult(Balances[retailerLoyaltyId]);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
