namespace FSCM.Application.Features.Retailers;

public sealed record RetailerDetails(
    Guid Id, Guid RegionId, Guid SalesGroupId, string SalesGroupCode, string Code, string Name,
    string? ContactName, string? Phone, string? Email, string? Address, string Status,
    string RetailerType, decimal ForecastRevenue, decimal StoreSizeSquareMeters,
    string StoreSizeCategory, decimal PurchasingCapacity, int PurchaseFrequencyPerMonth,
    int CreditTermDays, string PotentialClass, string? AssessmentNote, Guid DeclaredByUserId,
    Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, string? ReviewReason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record RetailerOrderEligibility(
    Guid SalesUserId, Guid RetailerId, bool IsEligible, Guid? SalesGroupId,
    string? SalesGroupCode, string RetailerStatus, int CreditTermDays,
    IReadOnlyCollection<string> Reasons);
