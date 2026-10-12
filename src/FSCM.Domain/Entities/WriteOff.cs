// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class WriteOff
{
    public Guid Id { get; set; }
    public string WriteOffCode { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

