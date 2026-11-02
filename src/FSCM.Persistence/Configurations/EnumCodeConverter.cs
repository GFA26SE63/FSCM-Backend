using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FSCM.Persistence.Configurations;

internal sealed class EnumCodeConverter(string family) : ValueConverter<string, short>(
    value => ToCode(family, value), value => FromCode(family, value))
{
    private static readonly IReadOnlyDictionary<string, string[]> Values =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SalesOrderStatus"] = ["Allocating", "Quoted", "AllocationFailed", "OutOfStock", "AllocationConflict", "Withdrawn", "QuoteRejected", "QuoteExpired", "PaymentPending", "ConvertedToPo"],
            ["PaymentMethod"] = ["Prepaid", "Credit"],
            ["QuoteStatus"] = ["Issued", "Accepted", "Rejected", "Expired"],
            ["PaymentStatus"] = ["Unpaid", "PartiallyPaid", "Paid", "Overdue"],
            ["AllocationAttemptStatus"] = ["Processing", "Succeeded", "OutOfStock", "Conflict", "TechnicalFailed"],
            ["ReservationStatus"] = ["Active", "Released", "Consumed", "Expired"],
            ["InventoryTransactionType"] = ["PurchaseReceipt", "CustomerReturnReceipt", "AdjustmentIncrease", "SalesIssue", "ReplacementIssue", "WriteOff", "AdjustmentDecrease", "OrderReserve", "OrderRelease"],
            ["ClaimStatus"] = ["Submitted", "UnderReview", "Rejected", "Approved", "Resolved"],
            ["ClaimResolutionType"] = ["Replacement", "FinancialAdjustment"],
            ["ReplacementStatus"] = ["Approved", "Preparing", "Shipped", "Received"],
            ["PickingStatus"] = ["Pending", "InProgress", "Completed", "Exception"]
        };

    internal static short ToCode(string family, string value)
    {
        var values = Values[family];
        var index = Array.IndexOf(values, value);
        return index >= 0 ? checked((short)index) : throw new InvalidOperationException($"'{value}' is not a valid {family}.");
    }

    internal static string FromCode(string family, short value)
    {
        var values = Values[family];
        return value >= 0 && value < values.Length ? values[value] : throw new InvalidOperationException($"'{value}' is not a valid {family} code.");
    }
}

internal sealed class NullableEnumCodeConverter(string family) : ValueConverter<string?, short?>(
    value => value == null ? null : EnumCodeConverter.ToCode(family, value),
    value => value == null ? null : EnumCodeConverter.FromCode(family, value.Value));
