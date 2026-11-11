namespace FSCM.API.Contracts.Complaints;

public sealed record CreateComplaintRequest(string OrderCode, string Sku, string LabelCode,
    decimal ActualQty, string? Note, string? EvidenceUrl,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null, Guid? AllocationId = null,
    string? IssueType = null);
public sealed record ResolveComplaintRequest(string Result, bool Approved = true);
public sealed record ApproveComplaintRequest(string? Note = null);
public sealed record RejectComplaintRequest(string Reason);
public sealed record RequestComplaintEvidenceRequest(string Reason);
