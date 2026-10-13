namespace FSCM.Modules.Pricing.Domain.BatchDiscounts;

/// <summary>Remaining shelf life at or below <paramref name="RemainingPercentAtMost"/> % → <paramref name="DiscountPercent"/> % off.</summary>
public sealed record DiscountTier(decimal RemainingPercentAtMost, decimal DiscountPercent);

public sealed record BatchForDiscount(string BatchCode, string Sku, DateOnly Expiry, int ShelfLifeDays, int Available);
public sealed record DiscountSuggestion(string BatchCode, string Sku, DateOnly Expiry, int Available, decimal RemainingPercent, decimal SuggestedPercent);

/// <summary>Suggestion rule used by "Quét tồn kho". Default tiers from the prototype: ≤60% → 5%, ≤40% → 10%, ≤30% → 15%.</summary>
public static class BatchDiscountPolicy
{
    public static readonly IReadOnlyList<DiscountTier> DefaultTiers = new[] { new DiscountTier(60, 5), new DiscountTier(40, 10), new DiscountTier(30, 15) };

    public static decimal RemainingPercent(DateOnly today, DateOnly expiry, int shelfLifeDays) =>
        shelfLifeDays <= 0 ? 0 : Math.Round((expiry.DayNumber - today.DayNumber) * 100m / shelfLifeDays, 1);

    public static IReadOnlyList<DiscountSuggestion> Suggest(DateOnly today, IEnumerable<BatchForDiscount> batches, IReadOnlyList<DiscountTier>? tiers = null)
    {
        var t = (tiers ?? DefaultTiers).OrderBy(x => x.RemainingPercentAtMost).ToList();
        var list = new List<DiscountSuggestion>();
        foreach (var b in batches.Where(x => x.Available > 0 && x.Expiry > today))
        {
            var rem = RemainingPercent(today, b.Expiry, b.ShelfLifeDays);
            var tier = t.FirstOrDefault(x => rem <= x.RemainingPercentAtMost);   // smallest threshold that still contains the batch = deepest discount
            if (tier is not null) list.Add(new DiscountSuggestion(b.BatchCode, b.Sku, b.Expiry, b.Available, rem, tier.DiscountPercent));
        }
        return list.OrderBy(x => x.Expiry).ThenBy(x => x.BatchCode, StringComparer.Ordinal).ToList();
    }
}
