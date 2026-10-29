namespace FSCM.Application.Features.InventoryLocks;

public static class InventoryLockStatuses
{
    /// <summary>Quantity is currently withheld from the issuable pool.</summary>
    public const string Active = "Active";

    /// <summary>Lock was lifted (e.g. false alarm) without consuming stock.</summary>
    public const string Released = "Released";

    /// <summary>Lock was resolved by a write-off document (F47) that reduced OnHand.</summary>
    public const string WrittenOff = "WrittenOff";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Active,
        Released,
        WrittenOff
    };
}

public static class InventoryLockReasons
{
    public const string Expired = "Expired";
    public const string Damaged = "Damaged";
    public const string QualityHold = "QualityHold";
    public const string Recall = "Recall";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Expired,
        Damaged,
        QualityHold,
        Recall,
        Other
    };
}
