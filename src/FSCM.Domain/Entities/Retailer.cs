// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Retailer
{
    public Guid Id { get; set; }
    public Guid RegionId { get; set; }
    public Guid? UserAccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RetailerType { get; set; } = string.Empty;
    public decimal? ForecastRevenue { get; set; }
    public decimal? StoreSizeSquareMeters { get; set; }
    public string StoreSizeCategory { get; set; } = string.Empty;
    public decimal? PurchasingCapacity { get; set; }
    public int PurchaseFrequencyPerMonth { get; set; }
    public int CreditTermDays { get; set; }
    public string? PotentialStatus { get; set; }
    public decimal? PotentialScore { get; set; }
    public string? AssessmentNote { get; set; }
    public Guid DeclaredByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public Guid? AssessedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
