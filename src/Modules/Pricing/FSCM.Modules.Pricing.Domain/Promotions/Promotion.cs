using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Pricing.Domain.Promotions;

public enum PromotionType { PerSku = 1, PerGroup = 2 }
public enum PromotionStatus { Active, Paused }

/// <summary>PerSku: quantity of one SKU reaches MinQuantity → Percent off that line.</summary>
public sealed class PromotionRule
{
    private PromotionRule() { }
    public PromotionRule(string sku, int minQuantity, decimal percent) { Sku = sku; MinQuantity = minQuantity; Percent = percent; }
    public string Sku { get; private set; } = "";
    public int MinQuantity { get; private set; }
    public decimal Percent { get; private set; }
}
public sealed class PromotionSku { private PromotionSku() { } public PromotionSku(string sku) => Sku = sku; public string Sku { get; private set; } = ""; }
public sealed class PromotionRetailer { private PromotionRetailer() { } public PromotionRetailer(string code) => RetailerCode = code; public string RetailerCode { get; private set; } = ""; }

public sealed record PromotionDetails(string Name, PromotionType Type, DateOnly ValidFrom, DateOnly ValidTo,
    IReadOnlyList<string> RetailerCodes,                       // empty = every retailer
    IReadOnlyList<PromotionRule> Rules,                        // PerSku
    IReadOnlyList<string> GroupSkus, string? CategoryCode,     // PerGroup: explicit SKUs and/or a whole category
    int GroupMinQuantity, decimal GroupPercent);

/// <summary>
/// Promotion KM-000. PerSku = "buy ≥ N of this SKU, get x%". PerGroup = "buy ≥ N cases across the SKUs of a group/category, get x% on each of them".
/// A line receives only the single best promotion (highest percent), never a stack.
/// </summary>
public sealed class Promotion : AggregateRoot<string>
{
    private readonly List<PromotionRule> _rules = new();
    private readonly List<PromotionSku> _groupSkus = new();
    private readonly List<PromotionRetailer> _retailers = new();
    private Promotion() { }
    public Promotion(string code, PromotionDetails d)
    {
        Guard.Against(!code.StartsWith("KM-", StringComparison.Ordinal), "promotion.code", "Mã khuyến mãi dạng KM-000.");
        Id = code; Status = PromotionStatus.Active; Apply(d);
    }
    public string Name { get; private set; } = "";
    public PromotionType Type { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }
    public PromotionStatus Status { get; private set; }
    public string? CategoryCode { get; private set; }
    public int GroupMinQuantity { get; private set; }
    public decimal GroupPercent { get; private set; }
    public IReadOnlyCollection<PromotionRule> Rules => _rules.AsReadOnly();
    public IReadOnlyCollection<PromotionSku> GroupSkus => _groupSkus.AsReadOnly();
    public IReadOnlyCollection<PromotionRetailer> Retailers => _retailers.AsReadOnly();

    public void Update(PromotionDetails d) => Apply(d);
    public void Pause() => Status = PromotionStatus.Paused;
    public void Resume() => Status = PromotionStatus.Active;

    public bool IsActiveOn(DateOnly date) => Status == PromotionStatus.Active && date >= ValidFrom && date <= ValidTo;
    public bool AppliesToRetailer(string retailerCode) => _retailers.Count == 0 || _retailers.Any(r => r.RetailerCode == retailerCode);
    public bool GroupCovers(string sku, Func<string, string?> categoryOf) =>
        _groupSkus.Any(x => x.Sku == sku) || (CategoryCode is not null && categoryOf(sku) == CategoryCode);

    private void Apply(PromotionDetails d)
    {
        Guard.Against(string.IsNullOrWhiteSpace(d.Name), "promotion.name", "Tên chương trình bắt buộc.");
        Guard.Against(d.ValidTo < d.ValidFrom, "promotion.dates", "Ngày kết thúc phải sau ngày bắt đầu.");
        if (d.Type == PromotionType.PerSku)
        {
            Guard.Against(d.Rules.Count == 0, "promotion.rules", "Cần ít nhất một SKU với ngưỡng và % giảm.");
            Guard.Against(d.Rules.Any(r => r.MinQuantity <= 0 || r.Percent <= 0 || r.Percent >= 100), "promotion.rules", "Ngưỡng > 0 và % giảm trong (0, 100).");
        }
        else
        {
            Guard.Against(d.GroupSkus.Count == 0 && string.IsNullOrWhiteSpace(d.CategoryCode), "promotion.group", "Cần danh sách SKU hoặc danh mục.");
            Guard.Against(d.GroupMinQuantity <= 0 || d.GroupPercent <= 0 || d.GroupPercent >= 100, "promotion.group", "Ngưỡng > 0 và % giảm trong (0, 100).");
        }
        Name = d.Name.Trim(); Type = d.Type; ValidFrom = d.ValidFrom; ValidTo = d.ValidTo; CategoryCode = d.CategoryCode;
        GroupMinQuantity = d.GroupMinQuantity; GroupPercent = d.GroupPercent;
        _rules.Clear(); _rules.AddRange(d.Type == PromotionType.PerSku ? d.Rules : Array.Empty<PromotionRule>());
        _groupSkus.Clear(); _groupSkus.AddRange(d.GroupSkus.Distinct().Select(s => new PromotionSku(s)));
        _retailers.Clear(); _retailers.AddRange(d.RetailerCodes.Distinct().Select(r => new PromotionRetailer(r)));
    }
}
