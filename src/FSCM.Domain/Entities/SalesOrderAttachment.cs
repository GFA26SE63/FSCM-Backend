// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class SalesOrderAttachment
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string AttachmentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}
