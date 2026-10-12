// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class BatchQrLabel
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string QrCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? BlobUrl { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
}

