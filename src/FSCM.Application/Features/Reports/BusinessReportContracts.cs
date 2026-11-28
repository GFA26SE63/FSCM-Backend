namespace FSCM.Application.Features.Reports;

public static class ReportPaginationLimits
{
    public const int MaxPeriodDays = 367;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

public sealed record BusinessPagedList<T>(
    IReadOnlyCollection<T> Items,
    int TotalCount,
    int Page,
    int PageSize);

// 1. FINANCIAL & CASHFLOW REPORT
public sealed record FinancialReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab = "orders",
    Guid? RegionId = null,
    Guid? SalesGroupId = null,
    string? Search = null,
    string? Status = null,
    string? PaymentMethod = null,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record FinancialSummary(
    int CreatedOrderCount,
    int CompletedOrderCount,
    decimal GrossOrderValue,
    decimal DiscountAmount,
    decimal ClaimAdjustmentAmount,
    decimal RecognizedRevenue,
    decimal CollectedAmount,
    decimal UnappliedCreditAmount,
    decimal RefundedAmount,
    decimal PendingRefundAmount,
    decimal WriteOffLossValue,
    bool WriteOffIncluded,
    decimal NetCashflow);

public sealed record FinancialOrderItem(
    Guid OrderId,
    string OrderCode,
    DateTimeOffset CreatedAt,
    DateOnly? RecognitionDate,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string SalesGroupCode,
    string SalesGroupName,
    string SalesUserName,
    string Status,
    string? PaymentMethod,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal AdjustmentAmount,
    decimal NetRecognizedAmount,
    decimal PaidAmount,
    decimal RemainingAmount);

public sealed record FinancialReceiptItem(
    Guid ReceiptId,
    string ReceiptCode,
    DateTimeOffset? ConfirmedAt,
    string Channel,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    decimal ReceivedAmount,
    decimal AllocatedAmount,
    decimal UnappliedCreditAmount,
    string? ConfirmedByName,
    string? Reference,
    string? Note);

public sealed record FinancialRefundItem(
    Guid RefundId,
    string RefundCode,
    Guid OrderId,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    decimal Amount,
    string Status,
    string Recipient,
    string Reason,
    DateTimeOffset CreatedAt,
    string? SubmittedByName,
    DateTimeOffset? SubmittedAt,
    string? VerifiedByName,
    DateTimeOffset? VerifiedAt,
    string? TransactionReference);

public sealed record FinancialReportResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab,
    string Scope,
    FinancialSummary Summary,
    BusinessPagedList<FinancialOrderItem> Orders,
    BusinessPagedList<FinancialReceiptItem> Receipts,
    BusinessPagedList<FinancialRefundItem> Refunds,
    IReadOnlyCollection<string> Notes);

// 2. RECEIVABLES & AGING REPORT
public sealed record ReceivablesReportQuery(
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    string View = "invoices",
    Guid? RegionId = null,
    Guid? SalesGroupId = null,
    string? Search = null,
    string? Bucket = null,
    bool IncludeSettled = false,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record ReceivablesAgingBucket(
    string Key,
    string Label,
    int InvoiceCount,
    decimal Amount);

public sealed record ReceivablesSummary(
    int OpenInvoiceCount,
    int SettledInvoiceCount,
    int DebtorRetailerCount,
    decimal TotalReceivable,
    decimal CurrentAmount,
    decimal Overdue1To7Amount,
    decimal Overdue8To30Amount,
    decimal Overdue31PlusAmount,
    decimal TotalOverdueAmount);

public sealed record ReceivableInvoiceItem(
    Guid PaymentId,
    Guid OrderId,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string SalesGroupCode,
    string SalesGroupName,
    string SalesUserName,
    DateTimeOffset OrderCreatedAt,
    DateOnly? DeliveredDate,
    DateOnly? DueDate,
    int DaysOverdue,
    string AgingBucket,
    decimal AdjustedAmount,
    decimal ConfirmedAmount,
    decimal OutstandingAmount,
    string PaymentStatus,
    string? PayOsLinkStatus,
    decimal CreditLimit,
    string? Note);

public sealed record ReceivableCustomerItem(
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string SalesGroupName,
    string SalesUserName,
    int OpenInvoices,
    int OverdueInvoices,
    decimal AdjustedTotal,
    decimal PaidTotal,
    decimal OutstandingTotal,
    decimal OverdueTotal,
    int MaxDaysOverdue,
    decimal CreditLimit,
    int CreditTermDays,
    decimal UnappliedCreditAmount);

public sealed record ReceivablesReportResult(
    DateOnly AsOfDate,
    string View,
    string Scope,
    ReceivablesSummary Summary,
    IReadOnlyCollection<ReceivablesAgingBucket> Buckets,
    BusinessPagedList<ReceivableInvoiceItem> Invoices,
    BusinessPagedList<ReceivableCustomerItem> Customers,
    IReadOnlyCollection<string> Notes);

// 3. PROMOTIONS & SHELF-LIFE DISCOUNT REPORT
public sealed record PromotionsReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? RegionId = null,
    string? Retailer = null,
    string? Program = null,
    string? ExactProgramCode = null,
    string? DiscountType = null,
    string? Status = null,
    bool CompletedOnly = false,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record PromotionsSummary(
    int AppliedOrderCount,
    int CompletedOrderCount,
    decimal TotalQuantity,
    decimal GrossAmount,
    decimal CampaignDiscountAmount,
    decimal ShelfLifeDiscountAmount,
    decimal TotalDiscountAmount,
    decimal NetAmount,
    decimal CompletedNetAmount,
    decimal CompletedDiscountAmount,
    decimal EffectiveDiscountRatePercent);

public sealed record PromotionProgramSummaryRow(
    string ProgramCode,
    string ProgramName,
    string DiscountType,
    int OrderCount,
    int CompletedOrderCount,
    decimal TotalQuantity,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal CampaignDiscountAmount,
    decimal ShelfLifeDiscountAmount,
    decimal NetAmount,
    decimal CompletedDiscountAmount,
    decimal CompletedNetAmount,
    decimal DiscountRatePercent);

public sealed record PromotionUsageItem(
    Guid Id,
    DateTimeOffset AcceptedAt,
    Guid OrderId,
    string OrderCode,
    string OrderStatus,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string DiscountType,
    string ProgramCode,
    string ProgramName,
    string Sku,
    string ProductName,
    string BatchCode,
    decimal Quantity,
    decimal ListUnitPrice,
    decimal GrossAmount,
    decimal CampaignDiscountAmount,
    decimal ShelfLifeDiscountAmount,
    decimal TotalDiscountAmount,
    decimal NetAmount,
    string Explanation);

public sealed record PromotionsReportResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Scope,
    PromotionsSummary Summary,
    IReadOnlyCollection<PromotionProgramSummaryRow> ByProgram,
    BusinessPagedList<PromotionUsageItem> Items,
    IReadOnlyCollection<string> Notes);

// 4. LOYALTY & REWARD REDEMPTIONS REPORT
public sealed record LoyaltyReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab = "redemptions",
    Guid? RegionId = null,
    string? Retailer = null,
    string? Program = null,
    string? Status = null,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record LoyaltyReportSummary(
    int TotalRedemptions,
    int PendingRedemptions,
    int CompletedRedemptions,
    int OverdueRedemptions,
    decimal RedeemedPointsCompleted,
    decimal GiftsDeliveredQuantity,
    decimal PeriodEarnedPoints,
    decimal PeriodUsedPoints,
    decimal PeriodExpiredPoints,
    decimal PeriodNetPointChange,
    decimal CurrentAvailablePoints,
    decimal CurrentHeldPoints,
    int ActiveCardCount);

public sealed record LoyaltyRedemptionItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string ProgramCode,
    string ProgramName,
    string RewardCode,
    string RewardName,
    int? Quantity,
    int RedeemedPoints,
    string Status,
    Guid? OrderId,
    string? OrderCode,
    Guid? TransactionId,
    string? CreatedByName,
    Guid? ApprovedByUserId,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    string? DeliveredByName,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? CancelledAt,
    string? Note);

public sealed record LoyaltyLedgerItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string ProgramCode,
    string ProgramName,
    string TransactionType,
    int Points,
    int BalanceAfter,
    Guid? OrderId,
    string? OrderCode,
    string? CreatedByName,
    string? Note);

public sealed record LoyaltyBalanceItem(
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string SalesGroupName,
    int CurrentPoints,
    int HeldPoints,
    int LifetimeEarnedPoints,
    int LifetimeRedeemedPoints,
    int PendingRedemptionsCount,
    DateTimeOffset? LastTransactionAt,
    DateTimeOffset? EarliestProgramExpiryAt);

public sealed record LoyaltyReportResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab,
    string Scope,
    LoyaltyReportSummary Summary,
    BusinessPagedList<LoyaltyRedemptionItem> Redemptions,
    BusinessPagedList<LoyaltyLedgerItem> Ledger,
    BusinessPagedList<LoyaltyBalanceItem> Balances,
    IReadOnlyCollection<string> Notes);

// 5. CLAIMS, RETURNS & AFTER-SALES REPORT
public sealed record ClaimsReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? RegionId = null,
    string? Retailer = null,
    string? Search = null,
    string? RequestType = null,
    string? SettlementMethod = null,
    string? Status = null,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record ClaimsSummary(
    int TotalClaims,
    int OpenClaims,
    int ResolvedClaims,
    int PhysicalCollectionPendingCount,
    decimal AverageResolutionHours,
    decimal RequestedQuantity,
    decimal ApprovedQuantity,
    decimal TotalDebtOffsetAmount,
    decimal TotalRefundAmount,
    decimal CompletedRefundAmount);

public sealed record ClaimsWarehouseBreakdown(
    string WarehouseCode,
    string WarehouseName,
    int ClaimCount,
    int OpenCount,
    decimal ApprovedQuantity,
    decimal FinancialImpactAmount);

public sealed record ClaimReportItem(
    Guid Id,
    string ClaimCode,
    DateTimeOffset ReportedAt,
    Guid OrderId,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string RegionName,
    string SalesGroupName,
    string SalesUserName,
    string WarehouseCode,
    string WarehouseName,
    string RequestType,
    string SettlementMethod,
    string ResolutionType,
    string Status,
    decimal RequestedQuantity,
    decimal? ApprovedQuantity,
    decimal? HandledQuantity,
    bool RequiresCollection,
    bool PhysicalHandlingCompleted,
    bool FinancialAdjustmentApplied,
    decimal DebtOffsetAmount,
    decimal RefundAmount,
    Guid? RefundId,
    string? RefundCode,
    string? RefundStatus,
    string? ReturnDocCode,
    string? ReturnDocStatus,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ResolvedAt,
    decimal? ResolutionHours,
    string? Note);

public sealed record ClaimsReportResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Scope,
    ClaimsSummary Summary,
    IReadOnlyCollection<ClaimsWarehouseBreakdown> ByWarehouse,
    BusinessPagedList<ClaimReportItem> Items,
    IReadOnlyCollection<string> Notes);

// 6. FULFILLMENT, RECEIVING & LOGISTICS SLA REPORT
public sealed record FulfillmentReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab = "shipments",
    Guid? RegionId = null,
    Guid? WarehouseId = null,
    string? Search = null,
    string? Status = null,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record FulfillmentSummary(
    int ShipmentCount,
    int DeliveredShipmentCount,
    int ExceptionShipmentCount,
    decimal FillRatePercent,
    decimal OnTimeRatePercent,
    decimal ShortageCartons,
    int? GoodsReceiptCount,
    decimal? GoodsReceiptCartons,
    int? PickingCount,
    int? PickingExceptionCount,
    int? TransferCount,
    int? TransferInTransitCount);

public sealed record FulfillmentShipmentItem(
    Guid ShipmentId,
    string ShipmentCode,
    Guid OrderId,
    string OrderCode,
    string WarehouseCode,
    string WarehouseName,
    string RetailerCode,
    string RetailerName,
    string? DriverName,
    string? PlateNumber,
    string Status,
    decimal TotalQuantity,
    decimal ReceivedQuantity,
    decimal ShortageQuantity,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? EstimatedArrivalAt,
    bool? IsOnTime,
    decimal? TransitHours,
    string? ExceptionReason);

public sealed record FulfillmentDriverItem(
    Guid DriverId,
    string DriverCode,
    string DriverName,
    string PlateNumber,
    string WarehouseCode,
    int TripCount,
    int CompletedTrips,
    int ExceptionTrips,
    int OnTimeTrips,
    decimal TotalCartons,
    decimal ReceivedCartons,
    decimal FillRatePercent,
    decimal OnTimeRatePercent);

public sealed record FulfillmentInboundItem(
    Guid DocumentId,
    string DocumentCode,
    string SourceType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string WarehouseCode,
    string WarehouseName,
    string RegionName,
    int LineCount,
    decimal ReceivedQuantity,
    string Status,
    decimal? LeadTimeHours,
    string? CreatedByName,
    string? Note);

public sealed record FulfillmentPickingItem(
    Guid PickingId,
    string PickingCode,
    Guid OrderId,
    string OrderCode,
    string WarehouseCode,
    string WarehouseName,
    string Status,
    int LineCount,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ConfirmedAt,
    decimal? PickingMinutes,
    string? AssigneeName,
    string? ConfirmedByName);

public sealed record FulfillmentTransferItem(
    Guid TransferId,
    string TransferCode,
    string SourceWarehouseCode,
    string SourceWarehouseName,
    string DestinationWarehouseCode,
    string DestinationWarehouseName,
    string Status,
    string? DriverName,
    string? PlateNumber,
    decimal RequestedQuantity,
    decimal DispatchedQuantity,
    decimal ReceivedQuantity,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    decimal? InTransitHours,
    string? Note);

public sealed record FulfillmentReportResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab,
    string Scope,
    FulfillmentSummary Summary,
    BusinessPagedList<FulfillmentShipmentItem> Shipments,
    BusinessPagedList<FulfillmentDriverItem> Drivers,
    BusinessPagedList<FulfillmentInboundItem> Inbound,
    BusinessPagedList<FulfillmentPickingItem> Picking,
    BusinessPagedList<FulfillmentTransferItem> Transfers,
    IReadOnlyCollection<string> Notes);

// 7. INVENTORY RISK, EXPIRY, LOCKS & WRITE-OFFS REPORT
public sealed record InventoryRiskReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab = "expiry",
    Guid? RegionId = null,
    Guid? WarehouseId = null,
    string? Search = null,
    string? Status = null,
    int Page = 1,
    int PageSize = ReportPaginationLimits.DefaultPageSize);

public sealed record InventoryRiskSummary(
    int NearExpiryBatchCount,
    decimal NearExpiryCartons,
    int ExpiredUnsoldBatchCount,
    decimal ExpiredUnsoldCartons,
    decimal EstimatedExpiryRiskValue,
    int ActiveLockCount,
    decimal ActiveLockedCartons,
    int LocksRequiringResolutionCount,
    decimal EstimatedLockedValue,
    int WriteOffDocCount,
    decimal DisposedCartonsInPeriod,
    decimal EstimatedWriteOffLossValue);

public sealed record ExpiryActionBatchItem(
    Guid BatchId,
    string BatchCode,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    string RegionName,
    string Sku,
    string ProductName,
    DateOnly ProductionDate,
    DateOnly ExpirationDate,
    int DaysUntilExpiry,
    string ExpiryStatus,
    decimal PhysicalQuantity,
    decimal ReservedQuantity,
    decimal LockedQuantity,
    decimal AvailableQuantity,
    bool HasActiveBatchPromo,
    decimal UnitPrice,
    decimal EstimatedAtRiskValue);

public sealed record InventoryLockReportItem(
    Guid LockId,
    string WarehouseCode,
    string WarehouseName,
    string RegionName,
    string BatchCode,
    string Sku,
    string ProductName,
    DateOnly ExpirationDate,
    decimal LockedQuantity,
    string Reason,
    bool RequiresResolution,
    string Status,
    int DaysOpen,
    string? LockedByName,
    string? ReleasedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReleasedAt,
    decimal EstimatedValue,
    string? Note);

public sealed record DisposalReportItem(
    Guid LineId,
    Guid WriteOffId,
    string WriteOffCode,
    DateTimeOffset CreatedAt,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    string RegionName,
    string BatchCode,
    string Sku,
    string ProductName,
    DateOnly? ExpirationDate,
    decimal Quantity,
    decimal UnitPriceAtWriteOff,
    decimal LossAmount,
    string DisposalMethod,
    string? CreatedByName,
    string? Note);

public sealed record InventoryRiskReportResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Tab,
    string Scope,
    InventoryRiskSummary Summary,
    BusinessPagedList<ExpiryActionBatchItem> Expiry,
    BusinessPagedList<InventoryLockReportItem> Locks,
    BusinessPagedList<DisposalReportItem> Disposals,
    IReadOnlyCollection<string> Notes);

public interface IBusinessReportStore
{
    Task<FinancialReportResult> GetFinancialAsync(FinancialReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
    Task<ReceivablesReportResult> GetReceivablesAsync(ReceivablesReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
    Task<PromotionsReportResult> GetPromotionsAsync(PromotionsReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
    Task<LoyaltyReportResult> GetLoyaltyAsync(LoyaltyReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
    Task<ClaimsReportResult> GetClaimsAsync(ClaimsReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
    Task<FulfillmentReportResult> GetFulfillmentAsync(FulfillmentReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
    Task<InventoryRiskReportResult> GetInventoryRiskAsync(InventoryRiskReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct);
}
