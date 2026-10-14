namespace FSCM.Domain.Authorization;

public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string SalesManager = "SalesManager";
    public const string Sales = "Sales";
    public const string WarehouseKeeper = "WarehouseKeeper";
    public const string Retailer = "Retailer";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Admin,
        Operator,
        SalesManager,
        Sales,
        WarehouseKeeper,
        Retailer
    };

    public static IReadOnlySet<string> Assignable { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Operator,
        SalesManager,
        Sales,
        WarehouseKeeper,
        Retailer
    };

    public static bool IsValid(string role) => All.Contains(role);

    public static bool CanBeAssignedThroughApi(string role) => Assignable.Contains(role);
}
