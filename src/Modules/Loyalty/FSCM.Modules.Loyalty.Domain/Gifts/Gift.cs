using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Loyalty.Domain.Gifts;

/// <summary>Quà tặng QUA-000 in the gift catalog.</summary>
public sealed class Gift : AggregateRoot<string>
{
    private Gift() { }
    public Gift(string code, string name, int pointCost, int stock, string? imageUrl)
    {
        Guard.Against(!code.StartsWith("QUA-", StringComparison.Ordinal), "gift.code", "Mã quà dạng QUA-000.");
        Id = code; Active = true; Update(name, pointCost, stock, imageUrl, true);
    }
    public string Name { get; private set; } = "";
    public int PointCost { get; private set; }
    public int Stock { get; private set; }
    public string? ImageUrl { get; private set; }
    public bool Active { get; private set; }
    public void Update(string name, int pointCost, int stock, string? imageUrl, bool active)
    {
        Guard.Against(string.IsNullOrWhiteSpace(name), "gift.name", "Tên quà bắt buộc.");
        Guard.Against(pointCost <= 0 || stock < 0, "gift.values", "Điểm đổi phải > 0 và tồn không âm.");
        Name = name.Trim(); PointCost = pointCost; Stock = stock; ImageUrl = imageUrl; Active = active;
    }
    public void TakeOne() { Guard.Against(!Active, "gift.inactive", $"Quà {Id} đã ngừng."); Guard.Against(Stock < 1, "gift.stock", $"Quà {Name} đã hết."); Stock--; }
    public void ReturnOne() => Stock++;
}
