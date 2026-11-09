// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; set; }
    public Guid ProductCategoryId { get; set; }
    public Guid? ReplacesProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int ShelfLifeHours { get; set; }
    public int NearDateThresholdHours { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Azure Blob key for the catalog image (nullable). Only the key is stored, not bytes.</summary>
    public string? ImageBlobKey { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? InactivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
