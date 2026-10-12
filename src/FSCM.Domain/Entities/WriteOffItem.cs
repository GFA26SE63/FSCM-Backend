// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class WriteOffItem
{
    public Guid Id { get; set; }
    public Guid WriteOffId { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

