using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Ordering.Domain.Complaints;

public enum ComplaintStatus { Pending, Resolved }

/// <summary>Khiếu nại KN-yyMMdd-nn from the retailer about a delivered order. Operator records the outcome.</summary>
public sealed class Complaint : AggregateRoot<string>
{
    private Complaint() { }
    public Complaint(string code, string orderCode, string retailerCode, string content, DateTime nowUtc)
    {
        Guard.Against(string.IsNullOrWhiteSpace(content), "complaint.content", "Nội dung khiếu nại bắt buộc.");
        Id = code; OrderCode = orderCode; RetailerCode = retailerCode; Content = content.Trim(); CreatedAtUtc = nowUtc; Status = ComplaintStatus.Pending;
    }
    public string OrderCode { get; private set; } = "";
    public string RetailerCode { get; private set; } = "";
    public string Content { get; private set; } = "";
    public ComplaintStatus Status { get; private set; }
    public string? Result { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public void Resolve(string result, string by, DateTime nowUtc)
    {
        Guard.Against(Status == ComplaintStatus.Resolved, "complaint.state", "Khiếu nại đã được xử lý.");
        Guard.Against(string.IsNullOrWhiteSpace(result), "complaint.result", "Cần ghi kết quả xử lý.");
        Result = result.Trim(); ResolvedBy = by; ResolvedAtUtc = nowUtc; Status = ComplaintStatus.Resolved;
    }
}
