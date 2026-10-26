namespace FSCM.Application.Features.Loyalty;

public interface ILoyaltyAccrualStore
{
    /// <summary>Everything one evaluation needs about an order, or null if the order is unknown.</summary>
    Task<LoyaltyAccrualView?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the retailer's balance row the first time they earn anything, so a card does not
    /// have to exist before the first order qualifies.
    /// </summary>
    Task<RetailerBalanceSnapshot> EnsureBalanceAsync(
        Guid retailerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Writes one ledger row and moves the balance by <paramref name="points"/>.
    ///
    /// The row and the balance move together or not at all - a ledger that disagrees with the
    /// balance is worse than either being wrong on its own, because there is then no way to tell
    /// which one to believe.
    /// </summary>
    Task<int> AppendAsync(
        Guid retailerLoyaltyId,
        Guid loyaltyProgramId,
        Guid? salesOrderId,
        Guid? actorUserId,
        string transactionType,
        int points,
        bool movesBalance,
        string note,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
