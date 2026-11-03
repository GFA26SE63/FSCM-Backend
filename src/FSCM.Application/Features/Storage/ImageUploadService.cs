namespace FSCM.Application.Features.Storage;

/// <summary>
/// Validates an incoming image (type, size, magic-number signature) and persists it through
/// <see cref="IImageStorage"/>. Shared by every feature that stores images so the rules live in one
/// place. Business services call this, then save the returned <see cref="StoredImage.BlobKey"/>.
/// </summary>
public sealed class ImageUploadService(IImageStorage storage)
{
    public async Task<StoredImage> ValidateAndUploadAsync(
        ImageCategory category, ImageUpload upload, CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured)
            throw ImageStorageException.Unavailable("Image storage is not configured on the server.");
        if (upload.SizeBytes <= 0)
            throw ImageStorageException.Validation("Ảnh rỗng.");
        if (upload.SizeBytes > ImageValidation.MaxBytes)
            throw ImageStorageException.Validation(
                $"Ảnh vượt quá {ImageValidation.MaxBytes / (1024 * 1024)} MB.");
        if (!ImageValidation.IsAllowedContentType(upload.ContentType))
            throw ImageStorageException.Validation("Chỉ chấp nhận ảnh JPG, JPEG, PNG hoặc WebP.");

        // Buffer so we can both sniff the header and re-send the full stream.
        var buffered = new MemoryStream();
        await upload.Content.CopyToAsync(buffered, cancellationToken);
        buffered.Position = 0;
        var header = new byte[12];
        var read = await buffered.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (!ImageValidation.SignatureMatches(header.AsSpan(0, read), upload.ContentType))
            throw ImageStorageException.Validation("Nội dung tệp không khớp với định dạng ảnh khai báo.");
        buffered.Position = 0;

        var sanitized = upload with
        {
            Content = buffered,
            ContentType = ImageValidation.NormalizeContentType(upload.ContentType),
            FileName = SanitizeFileName(upload.FileName),
            SizeBytes = buffered.Length
        };
        return await storage.UploadAsync(category, sanitized, cancellationToken);
    }

    public string GetReadUrl(ImageCategory category, string blobKey) =>
        storage.GetReadUrl(category, blobKey);

    public Task DeleteAsync(ImageCategory category, string blobKey, CancellationToken cancellationToken) =>
        storage.DeleteAsync(category, blobKey, cancellationToken);

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "image";
        var name = Path.GetFileName(fileName.Trim());
        return name.Length > 255 ? name[^255..] : name;
    }
}
