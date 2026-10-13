using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Loyalty.Domain.Programs;

/// <summary>The one rule for turning money into points: 1 point per <c>amountPerPoint</c> đồng of revenue after promotion, rounded down.</summary>
public static class PointCalculator
{
    public const decimal DefaultAmountPerPoint = 100_000m;
    public static int PointsFor(decimal netAmountAfterPromotion, decimal amountPerPoint = DefaultAmountPerPoint)
    {
        Guard.Against(amountPerPoint <= 0, "loyalty.rate", "Mức quy đổi phải lớn hơn 0.");
        return netAmountAfterPromotion <= 0 ? 0 : (int)Math.Floor(netAmountAfterPromotion / amountPerPoint);
    }
}

public sealed class ProgramRetailer { private ProgramRetailer() { } public ProgramRetailer(string code) => RetailerCode = code; public string RetailerCode { get; private set; } = ""; }

/// <summary>Chương trình tích điểm LP-000. Eligible retailers empty = all retailers.</summary>
public sealed class LoyaltyProgram : AggregateRoot<string>
{
    private readonly List<ProgramRetailer> _retailers = new();
    private LoyaltyProgram() { }
    public LoyaltyProgram(string code, string name, decimal amountPerPoint, DateOnly from, DateOnly to, int pointExpiryMonths, IEnumerable<string> retailerCodes)
    {
        Guard.Against(!code.StartsWith("LP-", StringComparison.Ordinal), "program.code", "Mã chương trình dạng LP-000.");
        Id = code; Active = true; Update(name, amountPerPoint, from, to, pointExpiryMonths, retailerCodes, true);
    }
    public string Name { get; private set; } = "";
    public decimal AmountPerPoint { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }
    /// <summary>Earned points expire this many months after they are earned (0 = never).</summary>
    public int PointExpiryMonths { get; private set; }
    public bool Active { get; private set; }
    public IReadOnlyCollection<ProgramRetailer> Retailers => _retailers.AsReadOnly();

    public void Update(string name, decimal amountPerPoint, DateOnly from, DateOnly to, int expiryMonths, IEnumerable<string> retailerCodes, bool active)
    {
        Guard.Against(string.IsNullOrWhiteSpace(name), "program.name", "Tên chương trình bắt buộc.");
        Guard.Against(amountPerPoint <= 0, "program.rate", "Mức quy đổi phải lớn hơn 0.");
        Guard.Against(to < from, "program.dates", "Ngày kết thúc phải sau ngày bắt đầu.");
        Guard.Against(expiryMonths < 0, "program.expiry", "Thời hạn điểm không âm.");
        Name = name.Trim(); AmountPerPoint = amountPerPoint; ValidFrom = from; ValidTo = to; PointExpiryMonths = expiryMonths; Active = active;
        _retailers.Clear(); _retailers.AddRange(retailerCodes.Distinct().Select(c => new ProgramRetailer(c)));
    }
    public bool IsActiveOn(DateOnly d) => Active && d >= ValidFrom && d <= ValidTo;
    public bool Covers(string retailerCode) => _retailers.Count == 0 || _retailers.Any(r => r.RetailerCode == retailerCode);
    public int PointsFor(decimal netAmountAfterPromotion) => PointCalculator.PointsFor(netAmountAfterPromotion, AmountPerPoint);
    public DateOnly? ExpiryFor(DateOnly earnedOn) => PointExpiryMonths == 0 ? null : earnedOn.AddMonths(PointExpiryMonths);
}
