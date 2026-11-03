using FSCM.Application.Features.Storage;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.WriteOffs;

/// <summary>
/// Manages write-off evidence images. Uploading, listing or removing an image is purely an evidence
/// operation: it never changes inventory. Stock changes still go through the write-off business flow.
/// </summary>
public sealed class WriteOffService(
    IWriteOffStore store, ImageUploadService images, TimeProvider timeProvider)
{
    private const ImageCategory Category = ImageCategory.WriteOffAttachment;

    public async Task<WriteOffAttachmentResult> AddAttachmentAsync(
        Guid writeOffId, Guid actor, ImageUpload upload, CancellationToken ct)
    {
        _ = await store.FindAsync(writeOffId, ct)
            ?? throw WriteOffOperationException.NotFound("Không tìm thấy phiếu xử lý giảm tồn.");
        var stored = await images.ValidateAndUploadAsync(Category, upload, ct);
        var attachment = new WriteOffAttachment
        {
            Id = Guid.NewGuid(),
            WriteOffId = writeOffId,
            UploadedByUserId = actor,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            BlobUrl = stored.BlobKey,
            UploadedAt = timeProvider.GetUtcNow()
        };
        await store.AddAttachmentAsync(attachment, ct);
        return ToResult(attachment);
    }

    public async Task<IReadOnlyCollection<WriteOffAttachmentResult>> ListAttachmentsAsync(
        Guid writeOffId, CancellationToken ct)
    {
        _ = await store.FindAsync(writeOffId, ct)
            ?? throw WriteOffOperationException.NotFound("Không tìm thấy phiếu xử lý giảm tồn.");
        var items = await store.ListAttachmentsAsync(writeOffId, ct);
        return items.Select(ToResult).ToArray();
    }

    public async Task RemoveAttachmentAsync(
        Guid writeOffId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await store.FindAttachmentAsync(writeOffId, attachmentId, ct)
            ?? throw WriteOffOperationException.NotFound("Không tìm thấy ảnh đính kèm.");
        await store.RemoveAttachmentAsync(attachment, ct);
        await images.DeleteAsync(Category, attachment.BlobUrl, ct);
    }

    // WriteOffAttachment.BlobUrl holds the blob KEY (schema predates the key/url split); resolve it
    // to a short-lived SAS URL on read.
    private WriteOffAttachmentResult ToResult(WriteOffAttachment a) => new(
        a.Id, a.FileName, a.ContentType, images.GetReadUrl(Category, a.BlobUrl), a.UploadedAt);
}
