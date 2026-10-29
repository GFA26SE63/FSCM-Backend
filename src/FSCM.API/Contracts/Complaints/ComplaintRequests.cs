namespace FSCM.API.Contracts.Complaints;

public sealed record CreateComplaintRequest(string OrderCode, string Sku, string LabelCode,
    decimal ActualQty, string? Note, string? EvidenceUrl,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null);
public sealed record ResolveComplaintRequest(string Result, bool Approved = true);
