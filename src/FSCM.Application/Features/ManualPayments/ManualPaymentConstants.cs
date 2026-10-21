namespace FSCM.Application.Features.ManualPayments;

public static class ManualPaymentMethods
{
    /// <summary>Cash on delivery - collected when goods are delivered.</summary>
    public const string CashOnDelivery = "COD";

    /// <summary>Prepaid by bank transfer before fulfillment.</summary>
    public const string PrepaidTransfer = "PrepaidTransfer";

    /// <summary>Prepaid in cash before fulfillment.</summary>
    public const string PrepaidCash = "PrepaidCash";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CashOnDelivery,
        PrepaidTransfer,
        PrepaidCash
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
