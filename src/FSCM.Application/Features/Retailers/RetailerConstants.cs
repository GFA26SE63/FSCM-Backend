namespace FSCM.Application.Features.Retailers;

public static class RetailerStatuses
{
    public const string PendingApproval = "PendingApproval";
    public const string Active = "Active";
    public const string Rejected = "Rejected";
    public const string Inactive = "Inactive";
}

public static class RetailerTypes
{
    public const string GroceryStore = "GroceryStore";
    public const string MiniSupermarket = "MiniSupermarket";
    public const string ConvenienceStore = "ConvenienceStore";
    public const string MarketStall = "MarketStall";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        GroceryStore, MiniSupermarket, ConvenienceStore, MarketStall
    };
}
