namespace FSCM.Application.Features.Retailers;

public sealed record RetailerDetails(
    Guid Id, Guid RegionId, Guid SalesGroupId, string SalesGroupCode, string Code, string Name,
    string? ContactName, string? Phone, string? Email, string? Address,
    decimal? Latitude, decimal? Longitude, string? ProviderPlaceId, int LocationVersion, string Status,
    string RetailerType, decimal ForecastRevenue, decimal StoreSizeSquareMeters,
    string StoreSizeCategory, decimal PurchasingCapacity, int PurchaseFrequencyPerMonth,
    int CreditTermDays, int? RequestedCreditTermDays, string PotentialClass, string? AssessmentNote, Guid DeclaredByUserId,
    Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, string? ReviewReason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    RetailerGeneralInformation GeneralInformation,
    IReadOnlyCollection<RetailerMlorDeclarationResult> MlorDeclarations,
    bool CanSubmitForReview);

public sealed record RetailerGeneralInformation(
    string Name, string? ContactName, string? Phone, string? Email, string? Address,
    decimal? Latitude, decimal? Longitude, string? ProviderPlaceId, int LocationVersion,
    string RetailerType, decimal ForecastRevenue, decimal StoreSizeSquareMeters,
    string StoreSizeCategory, decimal PurchasingCapacity, int PurchaseFrequencyPerMonth,
    int? RequestedCreditTermDays, string PotentialClass, string? AssessmentNote);

public sealed record RetailerMlorData(
    Guid? ProductCategoryId,
    Guid? ProductId,
    decimal MinimumLifeRatio,
    int? MinimumLifeHours);

public sealed record RetailerMlorDeclarationResult(
    Guid Id,
    Guid RetailerId,
    string Scope,
    Guid? ProductCategoryId,
    string? CategoryCode,
    string? CategoryName,
    Guid? ProductId,
    string? ProductSku,
    string? ProductName,
    decimal MinimumLifeRatio,
    int? MinimumLifeHours,
    int Version,
    Guid UpdatedByUserId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive);

public sealed record RetailerMlorResolutionResult(
    Guid RetailerId,
    Guid ProductId,
    string SourceScope,
    Guid? SourceRuleId,
    int SourceVersion,
    Guid? ProductCategoryId,
    decimal MinimumLifeRatio,
    int? MinimumLifeHours,
    DateTimeOffset ResolvedAt);

public sealed record RetailerPage(
    IReadOnlyCollection<RetailerDetails> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record RetailerOrderEligibility(
    Guid SalesUserId, Guid RetailerId, bool IsEligible, Guid? SalesGroupId,
    string? SalesGroupCode, string RetailerStatus, int CreditTermDays,
    IReadOnlyCollection<string> Reasons);
