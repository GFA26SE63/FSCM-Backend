namespace FSCM.API.Contracts.Complaints;

public sealed record CreateComplaintRequest(string OrderCode, string Sku, string LabelCode,
    decimal ActualQty, string? Note, string? EvidenceUrl);
public sealed record ResolveComplaintRequest(string Result);
