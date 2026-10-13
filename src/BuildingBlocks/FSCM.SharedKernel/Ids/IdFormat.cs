using System.Globalization;
using System.Text.RegularExpressions;

namespace FSCM.SharedKernel.Ids;

/// <summary>Document prefixes for {PN/PH/DC/KN/KMB}-{yyMMdd}-{nn}.</summary>
public enum DocumentType { GoodsReceipt, Disposal, Transfer, Complaint, BatchDiscount }

/// <summary>
/// Single place for every business ID rule agreed on 06/10/2026.
/// Sequences come from ISequenceGenerator (one row per key in table id_sequence).
/// </summary>
public static partial class IdFormat
{
    /// <summary>Retailer code = 8 digits. New retailers (approved KB requests) get 10 000 000 + sequence, e.g. 10004288.</summary>
    public static string Retailer(long n)
    {
        if (n <= 0 || n >= 90_000_000) throw new ArgumentOutOfRangeException(nameof(n));
        return (10_000_000 + n).ToString("00000000", CultureInfo.InvariantCulture);
    }
    public static string RetailerRequest(int n) => $"KB-{n:0000}";
    /// <summary>Sales group = area code + letter: Q12-A.</summary>
    public static string SalesGroup(string areaCode, int index) => $"{areaCode}-{(char)('A' + index)}";
    public static string Category(int n) => $"CAT-{n:00}";
    public static string Sku(int n) => $"SKU{n:000}";
    public static string Label(long n) => $"TEM-{n:000000}";
    public static string Order(long n) => $"DH-{n:000000}";
    public static string Promotion(int n) => $"KM-{n:000}";
    public static string LoyaltyProgram(int n) => $"LP-{n:000}";
    public static string Gift(int n) => $"QUA-{n:000}";
    public static string Driver(int n) => $"NG-{n:000}";

    /// <summary>Offline order created on a Sales device before sync: OFF-{device}-{0000}. A UUID idempotency key travels with it.</summary>
    public static string OfflineOrder(string deviceCode, int n) => $"OFF-{deviceCode}-{n:0000}";

    /// <summary>Picking list = order code + A/B/C… (one per warehouse).</summary>
    public static string Picking(string orderCode, int warehouseIndex) => orderCode + (char)('A' + warehouseIndex);

    /// <summary>Batch = {warehouse}-{sku}-{expiry ddMMyyyy}. Uses the expiry date, not the receipt date.</summary>
    public static string Batch(string warehouseCode, string sku, DateOnly expiry) => $"{warehouseCode}-{sku}-{expiry.ToString("ddMMyyyy", CultureInfo.InvariantCulture)}";

    public static string Document(DocumentType type, DateOnly date, int n) => $"{Prefix(type)}-{date.ToString("yyMMdd", CultureInfo.InvariantCulture)}-{n:00}";
    public static string GiftRequest(DateOnly date, int n) => $"DQ-{date.ToString("yyMMdd", CultureInfo.InvariantCulture)}-{n:00}";

    /// <summary>Sequence key so numbering restarts per day for dated documents.</summary>
    public static string DocumentSequenceKey(DocumentType type, DateOnly date) => $"{Prefix(type)}:{date:yyyyMMdd}";

    public static string Prefix(DocumentType t) => t switch
    {
        DocumentType.GoodsReceipt => "PN",
        DocumentType.Disposal => "PH",
        DocumentType.Transfer => "DC",
        DocumentType.Complaint => "KN",
        DocumentType.BatchDiscount => "KMB",
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    // ---- validation of codes that come from outside (SAP, number plates, admin input) ----
    public static bool IsRetailerCode(string s) => RetailerRx().IsMatch(s);
    public static bool IsWarehouseCode(string s) => WarehouseRx().IsMatch(s);
    public static bool IsVehiclePlate(string s) => PlateRx().IsMatch(s.Replace(" ", ""));
    public static bool IsAreaGroup(string s) => GroupRx().IsMatch(s);
    public static bool IsArea(string s) => AreaRx().IsMatch(s);
    public static bool IsSku(string s) => SkuRx().IsMatch(s);
    public static bool IsOrder(string s) => OrderRx().IsMatch(s);

    [GeneratedRegex(@"^\d{8}$")] private static partial Regex RetailerRx();
    [GeneratedRegex(@"^WH-[A-Z]{2,3}$")] private static partial Regex WarehouseRx();
    [GeneratedRegex(@"^\d{2}[A-Z]{1,2}-?\d{3}\.?\d{2}$")] private static partial Regex PlateRx();
    [GeneratedRegex(@"^[A-Z0-9]{2,4}-[A-Z]$")] private static partial Regex GroupRx();
    [GeneratedRegex(@"^[A-Z0-9]{2,4}$")] private static partial Regex AreaRx();
    [GeneratedRegex(@"^SKU\d{3,}$")] private static partial Regex SkuRx();
    [GeneratedRegex(@"^DH-\d{6}$")] private static partial Regex OrderRx();
}
