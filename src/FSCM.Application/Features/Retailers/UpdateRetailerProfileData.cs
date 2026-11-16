namespace FSCM.Application.Features.Retailers;

public sealed record UpdateRetailerProfileData(
    string Name, string ContactName, string Phone, string Email, string? Address,
    string RetailerType, decimal ForecastRevenue, decimal StoreSizeSquareMeters,
    decimal PurchasingCapacity, int PurchaseFrequencyPerMonth, int RequestedCreditTermDays,
    string? AssessmentNote, decimal? Latitude, decimal? Longitude, string? ProviderPlaceId);
