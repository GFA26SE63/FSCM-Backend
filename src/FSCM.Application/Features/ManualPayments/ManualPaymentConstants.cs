namespace FSCM.Application.Features.ManualPayments;

/// <summary>
/// Manual collection methods for a prepaid order. SRS BR-51 allows prepaid money only by bank
/// transfer, so cash on delivery and prepaid cash are not accepted. Credit receivables are
/// collected through the receivables API, not through manual payments.
/// </summary>
public static class ManualPaymentMethods
{
    /// <summary>Prepaid by bank transfer before fulfillment, verified from transfer evidence.</summary>
    public const string PrepaidTransfer = "PrepaidTransfer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PrepaidTransfer
    };
}

public static class ManualPaymentStatuses
{
    /// <summary>Record created, amount not yet collected/confirmed.</summary>
    public const string Pending = "Pending";

    /// <summary>Amount received and confirmed.</summary>
    public const string Paid = "Paid";

    /// <summary>Cancelled before confirmation.</summary>
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending,
        Paid,
        Cancelled
    };
}
