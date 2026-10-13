namespace FSCM.Modules.Pricing.Domain.Promotions;

public sealed record PriceLine(string Sku, int Quantity, decimal UnitPrice);
public sealed record PricedLine(int LineNo, string Sku, int Quantity, decimal UnitPrice, decimal Gross, string? PromotionCode, string? PromotionName, decimal Percent, decimal Discount)
{
    public decimal Net => Gross - Discount;
}
public sealed record AppliedPromotion(string Code, string Name, decimal Amount, string Detail);
public sealed record PromotionResult(IReadOnlyList<PricedLine> Lines, IReadOnlyList<AppliedPromotion> Applied)
{
    public decimal Gross => Lines.Sum(l => l.Gross);
    public decimal Discount => Lines.Sum(l => l.Discount);
    public decimal Net => Gross - Discount;
}

internal sealed class AppliedAcc(string name) { public string Name { get; } = name; public decimal Amount { get; set; } public List<string> Parts { get; } = new(); }

/// <summary>
/// Pure price calculation (same rules as the prototype and the mobile offline estimate):
/// per line pick the active promotion with the highest percent; discount = round(gross × percent / 100) in whole đồng.
/// </summary>
public static class PromotionCalculator
{
    public static PromotionResult Calculate(IEnumerable<Promotion> promotions, string retailerCode, DateOnly date,
        IReadOnlyList<PriceLine> lines, Func<string, string?> categoryOf)
    {
        var usable = promotions.Where(p => p.IsActiveOn(date) && p.AppliesToRetailer(retailerCode)).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        int QtyOf(string sku) => lines.Where(l => l.Sku == sku).Sum(l => l.Quantity);
        var priced = new List<PricedLine>();
        var applied = new Dictionary<string, AppliedAcc>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i]; var gross = l.UnitPrice * l.Quantity;
            Promotion? best = null; decimal bestPct = 0;
            foreach (var p in usable)
            {
                decimal pct = 0;
                if (p.Type == PromotionType.PerSku)
                {
                    var rule = p.Rules.FirstOrDefault(r => r.Sku == l.Sku);
                    if (rule is not null && l.Quantity >= rule.MinQuantity) pct = rule.Percent;
                }
                else if (p.GroupCovers(l.Sku, categoryOf))
                {
                    var total = lines.Where(x => p.GroupCovers(x.Sku, categoryOf)).Sum(x => x.Quantity);
                    if (total >= p.GroupMinQuantity) pct = p.GroupPercent;
                }
                if (pct > bestPct) { best = p; bestPct = pct; }
            }
            var discount = best is null ? 0 : Math.Round(gross * bestPct / 100m, 0, MidpointRounding.AwayFromZero);
            priced.Add(new PricedLine(i + 1, l.Sku, l.Quantity, l.UnitPrice, gross, best?.Id, best?.Name, bestPct, discount));
            if (best is not null)
            {
                if (!applied.TryGetValue(best.Id, out var acc)) applied[best.Id] = acc = new AppliedAcc(best.Name);
                acc.Parts.Add($"{l.Sku} × {l.Quantity} (−{bestPct:0.##}%)"); acc.Amount += discount;
            }
        }
        return new PromotionResult(priced, applied.Select(kv => new AppliedPromotion(kv.Key, kv.Value.Name, kv.Value.Amount, string.Join(", ", kv.Value.Parts))).ToList());
    }
}
