using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed record CreateComplaintData(string OrderCode, string Sku, string LabelCode,
    decimal ActualQuantity, string? Note, string? EvidenceUrl,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null, Guid? AllocationId = null,
    string? IssueType = null);
public sealed record ResolveComplaintData(string Result, bool Approved = true);

public sealed record ComplaintTarget(Guid ShipmentId, string ShipmentCode, Guid AllocationId,
    string Sku, string ProductName, string BatchCode, string LabelCode, decimal ExpectedQuantity);

/// <summary>One evidence image of a complaint. <see cref="Url"/> is a short-lived read SAS URL.</summary>
public sealed record ComplaintEvidenceResult(
    Guid Id, string FileName, string ContentType, string Url, DateTimeOffset UploadedAt);
public sealed record ComplaintResult(Guid Id, string Code, string OrderCode, string RetailerCode,
    string RetailerName, string Sku, string ProductName, decimal ExpectedQuantity,
    decimal ActualQuantity, string IssueType, string Status, string? LabelCode,
    string? Note, string? EvidenceUrl, DateTimeOffset CreatedAt, string? Response,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null, decimal FinalUnitPrice = 0,
    decimal AdjustmentAmount = 0, Guid SalesOrderId = default, Guid? AllocationId = null,
    bool FinancialAdjustmentApplied = false, string? ResolutionType = null,
    string ReviewStatus = "", string? EvidenceRequestReason = null);
