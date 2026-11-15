namespace FSCM.Application.Features.Reports;

public sealed record OperationsReportQuery(DateOnly FromDate, DateOnly ToDate,
    string? Retailer = null, string? Program = null, string? Status = null,
    Guid? RegionId = null, int Page = 1, int PageSize = 50);

public sealed class OperationsReportRow
{
    public Guid Id { get; init; }
    public DateTimeOffset At { get; init; }
    public Guid RetailerId { get; init; }
    public string RetailerCode { get; init; } = "";
    public string RetailerName { get; init; } = "";
    public Guid RegionId { get; init; }
    public string RegionName { get; init; } = "";
    public string ProgramCode { get; init; } = "";
    public string ProgramName { get; init; } = "";
    public string ItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal? Quantity { get; init; }
    public int Points { get; init; }
    public int? BalanceAfter { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal NetAmount { get; init; }
    public Guid? OrderId { get; init; }
    public string? OrderCode { get; init; }
    public Guid? TransactionId { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByName { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public string? ApprovedByName { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public Guid? DeliveredByUserId { get; init; }
    public string? DeliveredByName { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? Note { get; init; }
    public decimal CampaignDiscountAmount { get; init; }
    public decimal ShelfLifeDiscountAmount { get; init; }
}

public sealed record OperationsReportMetric(string Label, decimal Value);
public sealed record OperationsReportResult(IReadOnlyCollection<OperationsReportRow> Items,
    int TotalCount, int Page, int PageSize, IReadOnlyCollection<OperationsReportMetric> Summary,
    IReadOnlyCollection<string> Notes);

public interface IOperationsReportStore
{
    Task<OperationsReportResult> ReadAsync(string kind, OperationsReportQuery query, CancellationToken ct);
}
