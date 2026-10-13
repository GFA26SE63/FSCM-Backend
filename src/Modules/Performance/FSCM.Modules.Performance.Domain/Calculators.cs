using FSCM.Modules.Performance.Domain.Facts;
using FSCM.Modules.Performance.Domain.Kpi;
using FSCM.SharedKernel.Domain;
using System.Globalization;

namespace FSCM.Modules.Performance.Domain;

public sealed record DateRange(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly d) => d >= From && d <= To;
}

/// <summary>Period strings used by the API: "yyyy-MM" (month), "yyyy-Qn" (quarter), "yyyy" (year). Null / empty = month of <c>today</c>.</summary>
public static class Period
{
    public static DateRange Parse(string? period, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(period)) return Month(today.Year, today.Month);
        var p = period.Trim();
        if (p.Length == 4 && int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return new DateRange(new DateOnly(y, 1, 1), new DateOnly(y, 12, 31));
        if (p.Length == 7 && p[4] == '-' && char.ToUpperInvariant(p[5]) == 'Q' && int.TryParse(p[..4], out var qy) && int.TryParse(p[6..], out var q) && q is >= 1 and <= 4)
            return new DateRange(new DateOnly(qy, (q - 1) * 3 + 1, 1), new DateOnly(qy, q * 3, 1).AddMonths(1).AddDays(-1));
        if (p.Length == 7 && p[4] == '-' && int.TryParse(p[..4], out var my) && int.TryParse(p[5..], out var mm) && mm is >= 1 and <= 12) return Month(my, mm);
        throw new BusinessRuleException("period.invalid", "Kỳ báo cáo dạng yyyy-MM, yyyy-Qn hoặc yyyy.");
    }
    public static string MonthKey(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private static DateRange Month(int y, int m) => new(new DateOnly(y, m, 1), new DateOnly(y, m, 1).AddMonths(1).AddDays(-1));
}

public sealed record SalesRankRow(int Rank, string SalesUserId, int Orders, decimal Gross, decimal Revenue);
public sealed record ProductRankRow(int Rank, string Sku, int Cases, decimal Revenue);
public sealed record KpiScore(decimal RevenueAttainment, decimal NewStoreAttainment, decimal OrderAttainment, decimal Score);

public static class Rankings
{
    /// <summary>
    /// Ranking by revenue after promotion. Only orders closed by that Sales count (SalesUserId set) – an order created by an Operator
    /// for a retailer never raises a Sales' rank. Equal revenue shares a rank (1, 2, 2, 4).
    /// </summary>
    public static IReadOnlyList<SalesRankRow> Sales(IEnumerable<SalesOrderFact> facts, DateRange range)
    {
        var rows = facts.Where(f => f.ClosedBySales && range.Contains(f.CompletedOn)).GroupBy(f => f.SalesUserId!)
            .Select(g => (User: g.Key, Orders: g.Count(), Gross: g.Sum(f => f.Gross), Revenue: g.Sum(f => f.Net)))
            .OrderByDescending(r => r.Revenue).ThenBy(r => r.User, StringComparer.Ordinal).ToList();
        var result = new List<SalesRankRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var rank = i > 0 && rows[i].Revenue == rows[i - 1].Revenue ? result[i - 1].Rank : i + 1;
            result.Add(new SalesRankRow(rank, rows[i].User, rows[i].Orders, rows[i].Gross, rows[i].Revenue));
        }
        return result;
    }

    /// <summary>Top products by cases sold in the period (all completed orders).</summary>
    public static IReadOnlyList<ProductRankRow> Products(IEnumerable<SalesOrderFact> facts, DateRange range, int top = 10)
    {
        var rows = facts.Where(f => range.Contains(f.CompletedOn)).SelectMany(f => f.Lines).GroupBy(l => l.Sku)
            .Select(g => (Sku: g.Key, Cases: g.Sum(l => l.Quantity), Revenue: g.Sum(l => l.Net)))
            .OrderByDescending(r => r.Cases).ThenByDescending(r => r.Revenue).ThenBy(r => r.Sku, StringComparer.Ordinal).Take(top).ToList();
        return rows.Select((r, i) => new ProductRankRow(i + 1, r.Sku, r.Cases, r.Revenue)).ToList();
    }
}

public static class KpiCalculator
{
    /// <summary>Each indicator is capped at 120% of target so one outlier cannot hide misses elsewhere.</summary>
    public const decimal Cap = 1.2m;
    public static decimal Attainment(decimal actual, decimal target) => target <= 0 ? 0 : Math.Min(Cap, actual / target);

    public static KpiScore Score(SalesTarget t, decimal revenue, int newStores, int orders)
    {
        var r = Attainment(revenue, t.RevenueTarget); var n = Attainment(newStores, t.NewStoreTarget); var o = Attainment(orders, t.OrderTarget);
        return new KpiScore(r, n, o, Math.Round(r * t.WeightRevenue + n * t.WeightNewStores + o * t.WeightOrders, 4));
    }
}
