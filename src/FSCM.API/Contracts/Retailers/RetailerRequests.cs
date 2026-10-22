namespace FSCM.API.Contracts.Retailers;

public sealed record DeclareRetailerRequest(
    Guid RegionId, Guid SalesGroupId, string Name, string? ContactName, string? Phone,
    string? Email, string? Address, string RetailerType, decimal ForecastRevenue,
    decimal StoreSizeSquareMeters, decimal PurchasingCapacity, int PurchaseFrequencyPerMonth,
    int RequestedCreditTermDays, string? AssessmentNote,
    decimal? Latitude, decimal? Longitude, string? ProviderPlaceId,
    IReadOnlyCollection<RetailerMlorDeclarationRequest>? MlorDeclarations = null,
    bool SubmitForReview = false);

public sealed record UpdateRetailerDeclarationRequest(
    string Name, string? ContactName, string? Phone, string? Email, string? Address,
    string RetailerType, decimal ForecastRevenue, decimal StoreSizeSquareMeters,
    decimal PurchasingCapacity, int PurchaseFrequencyPerMonth, int RequestedCreditTermDays,
    string? AssessmentNote, decimal? Latitude, decimal? Longitude, string? ProviderPlaceId,
    IReadOnlyCollection<RetailerMlorDeclarationRequest>? MlorDeclarations = null,
    bool SubmitForReview = false);

public sealed record RetailerMlorDeclarationRequest(
    Guid? ProductCategoryId, Guid? ProductId, decimal MinimumLifeRatio, int? MinimumLifeHours);

public sealed record ReviewRetailerRequest(bool Approve, int? CreditTermDays, string? Reason);
public sealed record UpdateRetailerStatusRequest(bool IsActive);
