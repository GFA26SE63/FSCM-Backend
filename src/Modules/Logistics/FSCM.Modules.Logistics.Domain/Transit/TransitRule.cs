using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Logistics.Domain.Transit;

[Flags]
public enum RoutePurpose { WarehouseToRetailer = 1, WarehouseToWarehouse = 2 }

/// <summary>
/// One row of the shared transit table (CR-02). The same table serves retailer delivery and
/// warehouse-to-warehouse transfers; only the distance limit differs by purpose.
/// Rows are versioned by ValidFrom: editing the table inserts new rows, old orders keep their snapshot.
/// </summary>
public sealed class TransitRule : Entity<Guid>
{
    private TransitRule() { }
    public TransitRule(decimal fromKm, decimal toKm, int transitDays, decimal tripCost, RoutePurpose appliesTo, DateOnly validFrom)
    {
        Guard.Against(toKm <= fromKm, "transit.range", "Khoảng cách 'đến' phải lớn hơn 'từ'.");
        Guard.Against(transitDays < 1, "transit.days", "Số ngày vận chuyển tối thiểu là 1.");
        Id = Guid.NewGuid(); FromKm = fromKm; ToKm = toKm; TransitDays = transitDays; TripCost = tripCost; AppliesTo = appliesTo; ValidFrom = validFrom;
    }
    public decimal FromKm { get; private set; }
    public decimal ToKm { get; private set; }
    public int TransitDays { get; private set; }
    public decimal TripCost { get; private set; }
    public RoutePurpose AppliesTo { get; private set; }
    public DateOnly ValidFrom { get; private set; }

    /// <summary>First band includes 0 km: [0, 10]; later bands are (from, to].</summary>
    public bool Covers(decimal km) => FromKm == 0 ? km >= 0 && km <= ToKm : km > FromKm && km <= ToKm;
}

public sealed record TransitQuote(int TransitDays, decimal TripCost, string Band, decimal Km);

/// <summary>Distance limits by purpose. Defaults: Dmax_retailer 20 km, Dmax_kho 40 km.</summary>
public sealed record DistanceLimits(decimal DmaxRetailerKm = 20, decimal DmaxWarehouseKm = 40)
{
    public decimal For(RoutePurpose p) => p == RoutePurpose.WarehouseToWarehouse ? DmaxWarehouseKm : DmaxRetailerKm;
}

/// <summary>The rules in force on a given date. Pure logic, used by allocation (B2) and rotation (step 2).</summary>
public sealed class TransitTable
{
    private readonly IReadOnlyList<TransitRule> _rules;
    public DistanceLimits Limits { get; }
    public TransitTable(IEnumerable<TransitRule> rules, DistanceLimits limits)
    {
        _rules = rules.OrderBy(r => r.FromKm).ToList();
        Limits = limits;
    }

    /// <returns>null when the point is outside the served distance for this purpose.</returns>
    public TransitQuote? Quote(decimal km, RoutePurpose purpose)
    {
        if (km > Limits.For(purpose)) return null;
        var r = _rules.FirstOrDefault(x => x.AppliesTo.HasFlag(purpose) && x.Covers(km));
        return r is null ? null : new TransitQuote(r.TransitDays, r.TripCost, (r.FromKm == 0 ? "0" : "> " + r.FromKm) + " – " + r.ToKm + " km", km);
    }

    /// <summary>Seed values agreed on 09/10/2026.</summary>
    public static IReadOnlyList<TransitRule> Defaults(DateOnly validFrom) => new[]
    {
        new TransitRule(0, 10, 1, 300_000, RoutePurpose.WarehouseToRetailer | RoutePurpose.WarehouseToWarehouse, validFrom),
        new TransitRule(10, 20, 2, 500_000, RoutePurpose.WarehouseToRetailer | RoutePurpose.WarehouseToWarehouse, validFrom),
        new TransitRule(20, 40, 3, 800_000, RoutePurpose.WarehouseToWarehouse, validFrom),
    };
}
