namespace FSCM.Application.Features.Storage;

/// <summary>
/// Logical image storage buckets. Each maps to a private Azure Blob container; the exact container
/// name is resolved in the Infrastructure implementation from configuration.
/// </summary>
public enum ImageCategory
{
    /// <summary>Claim / complaint evidence photos (container <c>claim-evidence</c>).</summary>
    ClaimEvidence,

    /// <summary>Write-off evidence photos (container <c>write-off-attachments</c>).</summary>
    WriteOffAttachment,

    /// <summary>Retailer storefront photos (container <c>retailer-storefront-images</c>).</summary>
    RetailerStorefront,

    /// <summary>Product catalog images (container <c>product-images</c>).</summary>
    ProductImage
}

/// <summary>Result of persisting one image to blob storage.</summary>
/// <param name="BlobKey">Stable identifier stored in PostgreSQL: <c>{category-prefix}/{guid}{ext}</c>.</param>
public sealed record StoredImage(string BlobKey, string FileName, string ContentType, long SizeBytes);

/// <summary>Bytes of an image to upload, already read into memory and validated by the caller.</summary>
public sealed record ImageUpload(Stream Content, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Stores business images in private Azure Blob containers. Only a blob key + metadata are kept in
/// the database; the raw bytes never touch PostgreSQL. Read access is granted through short-lived
/// SAS URLs produced by <see cref="GetReadUrl"/> after the caller has checked authorization.
/// </summary>
public interface IImageStorage
{
    /// <summary>True when a connection string / account has been configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Uploads one image into the container for <paramref name="category"/>.</summary>
    Task<StoredImage> UploadAsync(ImageCategory category, ImageUpload upload, CancellationToken cancellationToken);

    /// <summary>Deletes a blob by key; safe to call when the blob is already gone.</summary>
    Task DeleteAsync(ImageCategory category, string blobKey, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a time-limited read-only SAS URL for <paramref name="blobKey"/>. The caller MUST have
    /// verified the requester is allowed to see this image before calling.
    /// </summary>
    string GetReadUrl(ImageCategory category, string blobKey);
}
