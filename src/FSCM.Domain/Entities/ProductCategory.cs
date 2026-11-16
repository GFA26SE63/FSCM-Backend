// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class ProductCategory
{
    public Guid Id { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    /// <summary>
    /// Category-level default MLOR, used when a retailer has no SKU/Category rule for the product.
    /// Null means the category falls back to its parent, then to the system default.
    /// </summary>
    public decimal? DefaultMlorRatio { get; set; }
    /// <summary>Bumped on every change so orders and offline drafts can detect a stale value.</summary>
    public int DefaultMlorVersion { get; set; }
    public DateTimeOffset? DefaultMlorUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
