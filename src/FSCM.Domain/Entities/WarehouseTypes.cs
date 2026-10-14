namespace FSCM.Domain.Entities;

public static class WarehouseTypes
{
    public const string Central = "Central";
    public const string Regional = "Regional";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Central,
        Regional
    };
}
