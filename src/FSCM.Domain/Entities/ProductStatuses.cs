namespace FSCM.Domain.Entities;

public static class ProductStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Inactive = "Inactive";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Draft,
        Published,
        Inactive
    };
}
