using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Complaints;

public sealed record CreateComplaintData(string OrderCode, string Sku, string LabelCode,
    decimal ActualQuantity, string? Note, string? EvidenceUrl,
    Guid? ShipmentId = null, Guid? AllocationPlanPriceId = null, Guid? AllocationId = null,
    string? IssueType = null, string RequestType = "Complaint", string SettlementMethod = "DebtOffset",
    decimal? AffectedQuantity = null, bool RequiresCollection = false, Guid? RequestId = null);
public sealed record ResolveComplaintData(string Result, bool Approved = true,
    decimal? ApprovedQuantity = null, string? SettlementMethod = null, int? ExpectedVersion = null,
    bool WaivePhysicalHandling = false, string? WaiverReason = null);
public sealed record CompleteRequestHandlingData(int ExpectedVersion, decimal ActualQuantity,
    DateTimeOffset CompletedAt, string Note, string EvidenceReference);
public sealed record ReturnHandlingDocumentResult(Guid Id, string Code, string Kind, string Status,
    decimal ApprovedQuantity, decimal? ActualQuantity, DateTimeOffset? CompletedAt, string? Note, string? EvidenceReference);

public sealed record ComplaintTarget(Guid ShipmentId, string ShipmentCode, Guid AllocationId,
    string Sku, string ProductName, string BatchCode, string LabelCode, decimal ExpectedQuantity,
    decimal ReceivedQuantity = 0, decimal RemainingClaimableQuantity = 0, decimal RemainingReturnableQuantity = 0);

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
    string ReviewStatus = "", string? EvidenceRequestReason = null)
{
    public string RequestType { get; init; } = "Complaint";
    public string SettlementMethod { get; init; } = "DebtOffset";
    public string? ApprovedSettlementMethod { get; init; }
    public decimal RequestedQuantity { get; init; }
    public decimal? ApprovedQuantity { get; init; }
    public decimal? HandledQuantity { get; init; }
    public bool RequiresCollection { get; init; }
    public bool PhysicalHandlingCompleted { get; init; }
    public decimal DebtOffsetAmount { get; init; }
    public decimal RefundAmount { get; init; }
    public Guid? RefundId { get; init; }
    public int Version { get; init; }
    public IReadOnlyCollection<ReturnHandlingDocumentResult> Documents { get; init; } = [];
}
