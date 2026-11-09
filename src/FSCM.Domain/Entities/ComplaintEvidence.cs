// Evidence images attached to a claim/complaint (ReceiptDiscrepancy). Added for Azure Blob storage:
// one complaint can carry many images, uploaded at creation or while UnderReview. Mirrors the
// WriteOffAttachment shape (FileName + ContentType + BlobKey + uploader + timestamp).

namespace FSCM.Domain.Entities;

public sealed class ComplaintEvidence
{
    public Guid Id { get; set; }
    public Guid ReceiptDiscrepancyId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Azure Blob key within the claim-evidence container. Raw bytes are never in the DB.</summary>
    public string BlobKey { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}
