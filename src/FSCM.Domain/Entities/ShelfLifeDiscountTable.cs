// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One published version of the near-expiry discount table. Editing publishes a new version instead
// of overwriting, so a run priced against an older version can still be explained.
public sealed class ShelfLifeDiscountTable
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
