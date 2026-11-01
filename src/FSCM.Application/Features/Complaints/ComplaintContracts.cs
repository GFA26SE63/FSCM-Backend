using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed record CreateComplaintData(string OrderCode, string Sku, string LabelCode,
    decimal ActualQuantity, string? Note, string? EvidenceUrl,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null);
public sealed record ResolveComplaintData(string Result, bool Approved = true);
public sealed record ComplaintResult(Guid Id, string Code, string OrderCode, string RetailerCode,
    string RetailerName, string Sku, string ProductName, decimal ExpectedQuantity,
    decimal ActualQuantity, string IssueType, string Status, string? LabelCode,
    string? Note, string? EvidenceUrl, DateTimeOffset CreatedAt, string? Response,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null, decimal FinalUnitPrice = 0,
    decimal AdjustmentAmount = 0, Guid SalesOrderId = default);
