// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// Why one order line came out the way it did, frozen against one plan version. The weights and the
// shelf life MLOR demanded are copied here rather than read from configuration later: a weight
// changed next week must not restate a decision already taken.
//
// This carries no price. Batches are chosen first and priced afterwards (F89), so the reason a
// batch was picked and the reason it was reduced are deliberately kept apart. The price for the
// same (plan version, line) lives in ALLOCATION_PLAN_PRICE.
public sealed class AllocationLineTrace
{
    public Guid Id { get; set; }
    public Guid AllocationPlanVersionId { get; set; }
    public Guid AllocationRunLineId { get; set; }

    /// <summary>Allocated, Short, or Released once the order gave up and handed the stock back.</summary>
    public string Outcome { get; set; } = string.Empty;

    public decimal OrderedQuantity { get; set; }

    /// <summary>What this line drew. On a Released line this is what went back to the pool.</summary>
    public decimal DrawnQuantity { get; set; }
    public decimal ShortageQuantity { get; set; }

    /// <summary>MLOR expressed in hours, which is what candidates were actually measured against.</summary>
    public decimal RequiredShelfLifeHours { get; set; }

    public decimal ShelfLifeWeight { get; set; }
    public decimal DistanceWeight { get; set; }
    public decimal CoverageWeight { get; set; }
    public decimal MaximumDistanceMeters { get; set; }

    /// <summary>Set on the line that fell short, and on the lines released because of it.</summary>
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
