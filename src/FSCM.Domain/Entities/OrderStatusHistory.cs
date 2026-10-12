// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class OrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
