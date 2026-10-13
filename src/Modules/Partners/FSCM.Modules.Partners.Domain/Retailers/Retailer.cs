using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Partners.Domain.Retailers;

/// <summary>Potential fields declared by Sales / maintained by Admin (forecast revenue, store size, purchasing capacity…).</summary>
public sealed record RetailerPotential(decimal ForecastMonthlyRevenue, decimal StoreSizeM2, int FridgeCount, int PurchasingCapacityCases, string VisitFrequency, string Size, string Class)
{
    public static readonly RetailerPotential Unknown = new(0, 0, 0, 0, "", "", "C");
}

/// <summary>Retailer-specific MLOR ratio for one SKU (MLOR = m × SL).</summary>
public sealed class RetailerSkuMlor
{
    private RetailerSkuMlor() { }
    public RetailerSkuMlor(string sku, decimal ratio) { Sku = sku; Ratio = ratio; }
    public string Sku { get; private set; } = "";
    public decimal Ratio { get; private set; }
    internal void Change(decimal ratio) => Ratio = ratio;
}

public sealed class Retailer : AggregateRoot<string>
{
    public static readonly int[] AllowedCreditTerms = { 0, 7, 14, 30 };
    private readonly List<RetailerSkuMlor> _mlor = new();
    private Retailer() { }
    public Retailer(string code, string name, string phone, string address, double lat, double lng, string areaCode, string kind, RetailerPotential potential, string source, DateTime nowUtc)
    {
        Guard.Against(!IdFormat.IsRetailerCode(code), "retailer.code", "Mã retailer gồm 8 chữ số.");
        Id = code; Source = source; CreatedAtUtc = nowUtc; Active = true;
        UpdateProfile(name, phone, address, lat, lng, areaCode, kind, potential, nowUtc);
    }
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string Address { get; private set; } = "";
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public string AreaCode { get; private set; } = "";
    public string Kind { get; private set; } = "";
    public RetailerPotential Potential { get; private set; } = RetailerPotential.Unknown;
    /// <summary>0 = no credit; otherwise the credit term the retailer is allowed (7/14/30 days).</summary>
    public int CreditTermDays { get; private set; }
    public bool Active { get; private set; }
    public string Source { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<RetailerSkuMlor> Mlor => _mlor.AsReadOnly();

    public void UpdateProfile(string name, string phone, string address, double lat, double lng, string areaCode, string kind, RetailerPotential potential, DateTime nowUtc)
    {
        Guard.Against(string.IsNullOrWhiteSpace(name), "retailer.name", "Tên điểm bán bắt buộc.");
        Guard.Against(lat is < -90 or > 90 || lng is < -180 or > 180, "retailer.gps", "Toạ độ GPS không hợp lệ.");
        Name = name.Trim(); Phone = phone; Address = address; Lat = lat; Lng = lng; AreaCode = areaCode; Kind = kind; Potential = potential; UpdatedAtUtc = nowUtc;
    }

    public void SetCreditTerm(int days, DateTime nowUtc)
    {
        Guard.Against(!AllowedCreditTerms.Contains(days), "retailer.credit_term", "Kỳ hạn công nợ chỉ được 0 (không cho nợ), 7, 14 hoặc 30 ngày.");
        CreditTermDays = days; UpdatedAtUtc = nowUtc;
    }

    public void SetMlor(string sku, decimal ratio, DateTime nowUtc)
    {
        Guard.Against(ratio <= 0 || ratio >= 1, "retailer.mlor", "Tỷ lệ MLOR trong khoảng (0, 1).");
        var row = _mlor.FirstOrDefault(m => m.Sku == sku);
        if (row is null) _mlor.Add(new RetailerSkuMlor(sku, ratio)); else row.Change(ratio);
        UpdatedAtUtc = nowUtc;
    }
    public void RemoveMlor(string sku) => _mlor.RemoveAll(m => m.Sku == sku);
    public decimal? MlorFor(string sku) => _mlor.FirstOrDefault(m => m.Sku == sku)?.Ratio;
    public bool CanBuyOnCredit(int termDays) => termDays > 0 && CreditTermDays > 0 && termDays <= CreditTermDays;
    public void SetActive(bool active, DateTime nowUtc) { Active = active; UpdatedAtUtc = nowUtc; }
}
