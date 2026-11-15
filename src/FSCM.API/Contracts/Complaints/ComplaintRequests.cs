namespace FSCM.API.Contracts.Complaints;

public sealed record CreateComplaintRequest(string OrderCode, string Sku, string LabelCode,
    decimal ActualQty, string? Note, string? EvidenceUrl,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null, Guid? AllocationId = null,
    string? IssueType = null, string RequestType = "Complaint", string SettlementMethod = "DebtOffset",
    decimal? AffectedQuantity = null, bool RequiresCollection = false, Guid? RequestId = null);
public sealed record ResolveComplaintRequest(string Result, bool Approved = true, decimal? ApprovedQuantity = null,
    string? SettlementMethod = null, int? ExpectedVersion = null, bool WaivePhysicalHandling = false, string? WaiverReason = null);
public sealed record ApproveComplaintRequest(string? Note = null, decimal? ApprovedQuantity = null,
    string? SettlementMethod = null, int? ExpectedVersion = null, bool WaivePhysicalHandling = false, string? WaiverReason = null);
public sealed record CompleteRequestHandlingRequest(int ExpectedVersion, decimal ActualQuantity,
    DateTimeOffset CompletedAt, string Note, string EvidenceReference);
public sealed record RejectComplaintRequest(string Reason, int? ExpectedVersion = null);
public sealed record RequestComplaintEvidenceRequest(string Reason);
