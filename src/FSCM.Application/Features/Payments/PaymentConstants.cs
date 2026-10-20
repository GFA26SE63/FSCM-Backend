namespace FSCM.Application.Features.Payments;

public static class PaymentMethods
{
    /// <summary>Cash on delivery - payment collected when goods are delivered.</summary>
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

    public static bool IsPrepaid(string method) =>
        method is PrepaidTransfer or PrepaidCash;
}

public static class PaymentStatuses
{
    /// <summary>Payment record created, amount not yet collected/confirmed.</summary>
    public const string Pending = "Pending";

    /// <summary>Payment amount has been received and confirmed.</summary>
    public const string Paid = "Paid";

    /// <summary>Payment was cancelled before confirmation.</summary>
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending,
        Paid,
        Cancelled
    };
}
