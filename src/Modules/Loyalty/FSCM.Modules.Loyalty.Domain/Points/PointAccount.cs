using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Loyalty.Domain.Points;

public enum LedgerStatus { Pending, Earned, Held, Refunded }

/// <summary>One line of the point history. Points is signed: Earned +, Held −, Refunded +. Pending is not part of the balance until it becomes Earned.</summary>
public sealed class PointLedgerEntry : Entity<Guid>
{
    private PointLedgerEntry() { }
    public PointLedgerEntry(string retailerCode, int points, LedgerStatus status, string reference, string note, string? programCode, DateOnly? expiresOn, DateTime nowUtc)
    { Id = Guid.NewGuid(); RetailerCode = retailerCode; Points = points; Status = status; Reference = reference; Note = note; ProgramCode = programCode; ExpiresOn = expiresOn; CreatedAtUtc = nowUtc; }
    public string RetailerCode { get; private set; } = "";
    public int Points { get; private set; }
    public LedgerStatus Status { get; private set; }
    /// <summary>Order code (DH-…) for order points, redemption code (DQ-…) for hold / refund.</summary>
    public string Reference { get; private set; } = "";
    public string Note { get; private set; } = "";
    public string? ProgramCode { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? EarnedAtUtc { get; private set; }
    /// <summary>Credit order paid: Pending → Earned.</summary>
    public void Earn(DateOnly? expiresOn, DateTime nowUtc)
    {
        Guard.Against(Status != LedgerStatus.Pending, "ledger.state", "Chỉ điểm Chờ cộng mới chuyển thành Đã cộng.");
        Status = LedgerStatus.Earned; ExpiresOn = expiresOn; EarnedAtUtc = nowUtc;
    }
}

/// <summary>
/// Running balance of a retailer. The Version token serialises concurrent redemptions: two gift requests that together exceed the balance
/// cannot both succeed.
/// </summary>
public sealed class PointAccount : AggregateRoot<string>
{
    private PointAccount() { }
    public PointAccount(string retailerCode) { Id = retailerCode; }
    public int Available { get; private set; }
    public int Pending { get; private set; }
    public int Held { get; private set; }

    public void AddPending(int points) { Guard.Against(points <= 0, "points.value", "Số điểm phải lớn hơn 0."); Pending += points; }
    public void AddEarned(int points) { Guard.Against(points <= 0, "points.value", "Số điểm phải lớn hơn 0."); Available += points; }
    public void ConfirmPending(int points) { Guard.Against(points > Pending, "points.pending", "Điểm chờ cộng không đủ."); Pending -= points; Available += points; }
    public void Hold(int points)
    {
        Guard.Against(points <= 0, "points.value", "Số điểm phải lớn hơn 0.");
        Guard.Against(points > Available, "points.insufficient", $"Không đủ điểm: cần {points}, khả dụng {Available}.");
        Available -= points; Held += points;
    }
    public void Refund(int points) { Guard.Against(points > Held, "points.held", "Điểm đang giữ không đủ để hoàn."); Held -= points; Available += points; }
    public void Spend(int points) { Guard.Against(points > Held, "points.held", "Điểm đang giữ không đủ."); Held -= points; }
}
