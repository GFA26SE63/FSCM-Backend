namespace FSCM.Application.Features.Loyalty;

/// <summary>
/// The kinds of row the points ledger carries.
///
/// These were loose strings spread across the stores and the card service, which is how
/// <c>"PendingEarn"</c> and <c>"Pending"</c> both came to mean the same thing and both had to be
/// summed to get one number. They are named here so the next kind is added in one place.
/// </summary>
public static class LoyaltyTransactionTypes
{
    /// <summary>Points earned by an order that has met every condition. Adds to the balance.</summary>
    public const string Earned = "Earned";

    /// <summary>
    /// What an order will earn once it qualifies. Carries no balance: it exists so the card can
    /// show "Chờ cộng" without a second table, and is cancelled by its own reversal when the
    /// order either earns or stops being able to.
    /// </summary>
    public const string PendingEarn = "PendingEarn";

    /// <summary>Cancels a <see cref="PendingEarn"/> row, leaving both on the ledger.</summary>
    public const string PendingEarnCancelled = "PendingEarnCancelled";

    /// <summary>
    /// A correction after the order's value changed - an adjustment, a refund, a complaint settled
    /// in the retailer's favour. Linked to the same order, and may take the balance negative.
    /// </summary>
    public const string EarnedAdjusted = "EarnedAdjusted";

    public const string RedemptionHeld = "RedemptionHeld";
    public const string RedemptionRejected = "RedemptionRejected";
    public const string RedemptionCompleted = "RedemptionCompleted";
    public const string Expired = "Expired";
}

/// <summary>Why an order has not earned its points yet, in the order the conditions are checked.</summary>
public enum LoyaltyAccrualBlock
{
    None,
    NoActiveProgram,
    NotFullyReceived,
    NotFullyPaid,
    ComplaintOpen,
    NotCompleted,
    NothingToEarn,
}

/// <summary>
/// What an evaluation decided about one order.
///
/// <see cref="Points"/> is what the order is worth either way, so a caller can show "Chờ cộng
/// <c>n</c> điểm" from the same answer that would have awarded them.
/// </summary>
public sealed record LoyaltyAccrualResult(
    Guid SalesOrderId,
    bool Earned,
    int Points,
    int BalanceAfter,
    LoyaltyAccrualBlock Block,
    string Explanation);

/// <summary>Everything one evaluation needs, read in a single pass.</summary>
public sealed record LoyaltyAccrualView(
    Guid SalesOrderId,
    Guid RetailerId,
    string OrderStatus,
    decimal ApprovedFinalAmount,
    bool PaymentSettled,
    bool HasOpenComplaint,
    LoyaltyProgramSnapshot? Program,
    RetailerBalanceSnapshot? Balance,
    bool AlreadyEarned,
    bool HasLivePending);

public sealed record LoyaltyProgramSnapshot(Guid Id, decimal SpendAmountPerPoint);

public sealed record RetailerBalanceSnapshot(Guid Id, int CurrentPoints, int LifetimeEarnedPoints);
