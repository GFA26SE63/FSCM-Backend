using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Loyalty.Domain.Gifts;

public enum RedemptionStatus { Requested, Approved, Delivered, Done, Rejected }

/// <summary>
/// Yêu cầu đổi quà DQ-yyMMdd-nn. Requested (points held) → Approved → Delivered ("Đang giao quà") → Done; Rejected from Requested / Approved refunds the points.
/// </summary>
public sealed class GiftRedemption : AggregateRoot<string>
{
    private GiftRedemption() { }
    public GiftRedemption(string code, string retailerCode, string giftCode, string giftName, int points, DateTime nowUtc)
    { Id = code; RetailerCode = retailerCode; GiftCode = giftCode; GiftName = giftName; Points = points; RequestedAtUtc = nowUtc; Status = RedemptionStatus.Requested; }
    public string RetailerCode { get; private set; } = "";
    public string GiftCode { get; private set; } = "";
    public string GiftName { get; private set; } = "";
    public int Points { get; private set; }
    public RedemptionStatus Status { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public string? DecidedBy { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void Approve(string by, DateTime now) { Move(RedemptionStatus.Requested, RedemptionStatus.Approved, now); DecidedBy = by; }
    public void Reject(string by, string reason, DateTime now)
    {
        Guard.Against(Status is not (RedemptionStatus.Requested or RedemptionStatus.Approved), "redemption.state", "Chỉ từ chối yêu cầu chưa giao quà.");
        Guard.Against(string.IsNullOrWhiteSpace(reason), "redemption.reason", "Cần nhập lý do từ chối.");
        Status = RedemptionStatus.Rejected; DecidedBy = by; RejectReason = reason; UpdatedAtUtc = now;
    }
    public void Deliver(DateTime now) => Move(RedemptionStatus.Approved, RedemptionStatus.Delivered, now);
    public void Complete(DateTime now) => Move(RedemptionStatus.Delivered, RedemptionStatus.Done, now);
    private void Move(RedemptionStatus from, RedemptionStatus to, DateTime now)
    {
        Guard.Against(Status != from, "redemption.state", $"Yêu cầu {Id} đang ở trạng thái {Status}, không thể chuyển sang {to}.");
        Status = to; UpdatedAtUtc = now;
    }
}
