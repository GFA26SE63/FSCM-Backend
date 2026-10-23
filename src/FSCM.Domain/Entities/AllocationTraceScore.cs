// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One eligible batch as it was ranked in one greedy turn. Every candidate of every turn is kept,
// not just the winner: the score only means something next to the scores it beat.
//
// Coverage is recomputed each turn because the shortfall shrinks, so the same batch can score
// differently in turn one and turn two. That is why the turn number is part of the row.
public sealed class AllocationTraceScore
{
    public Guid Id { get; set; }
    public Guid AllocationLineTraceId { get; set; }

    /// <summary>The greedy turn, from one. Coverage is measured against the shortfall at its start.</summary>
    public int Iteration { get; set; }

    /// <summary>Rank within the turn, from one. Ties break on batch code.</summary>
    public int Rank { get; set; }

    public Guid BatchId { get; set; }
    public string BatchCodeSnapshot { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseCodeSnapshot { get; set; } = string.Empty;

    /// <summary>What was still missing when this turn began.</summary>
    public decimal RemainingDemand { get; set; }

    public decimal ShelfLifeScore { get; set; }
    public decimal DistanceScore { get; set; }
    public decimal CoverageScore { get; set; }
    public decimal WeightedScore { get; set; }
    public bool Selected { get; set; }

    /// <summary>What the winner of this turn took. Zero on every other row.</summary>
    public decimal SelectedQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
