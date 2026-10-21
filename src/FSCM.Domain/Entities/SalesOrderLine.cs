// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// Line type, accepted offer version, and effective MLOR follow the FEFO v3 section 4.7 contract
// in FSCM.Application/Features/NearExpiryOffers/README.md. MLOR is never derived from price.
public sealed class SalesOrderLine
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid ProductId { get; set; }
    public int LineNumber { get; set; }
    public string LineType { get; set; } = string.Empty;
    public Guid? NearExpiryOfferId { get; set; }
    public int? NearExpiryOfferVersion { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? DiscountSourceType { get; set; }
    public string? DiscountSourceCode { get; set; }
    public decimal NetAmount { get; set; }
    public decimal? AcceptedNetUnitPrice { get; set; }
    public decimal EffectiveMlorRatio { get; set; }
    public string MlorSource { get; set; } = string.Empty;
    public decimal? DefaultMlorRatio { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? EstimatedArrivalAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
