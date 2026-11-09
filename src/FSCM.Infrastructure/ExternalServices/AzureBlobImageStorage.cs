using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FSCM.Application.Features.Storage;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.ExternalServices;

/// <summary>
/// <see cref="IImageStorage"/> backed by Azure Blob Storage. Containers are PRIVATE: images are
/// never served anonymously. The database stores only the blob key (<c>{category}/{guid}{ext}</c>);
/// read access is a short-lived user-delegation-free SAS built from the account key, produced only
/// after the calling service has verified authorization.
/// </summary>
public sealed class AzureBlobImageStorage : IImageStorage
{
    private readonly FscmBlobStorageOptions _options;
    private readonly BlobServiceClient? _client;

    public AzureBlobImageStorage(IOptions<FscmBlobStorageOptions> options)
    {
        _options = options.Value;
        _client = string.IsNullOrWhiteSpace(_options.ConnectionString)
            ? null
            : new BlobServiceClient(_options.ConnectionString);
    }

    public bool IsConfigured => _client is not null;

    public async Task<StoredImage> UploadAsync(
        ImageCategory category, ImageUpload upload, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(category, cancellationToken);
        var extension = ImageValidation.ExtensionFor(upload.ContentType);
        var contentType = ImageValidation.NormalizeContentType(upload.ContentType);
        // Blob key is opaque and prefixed by category so one account can hold every group cleanly.
        var blobKey = $"{CategoryPrefix(category)}/{Guid.NewGuid():N}{extension}";
        var blob = container.GetBlobClient(blobKey);
        await blob.UploadAsync(
            upload.Content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
        return new StoredImage(blobKey, upload.FileName, contentType, upload.SizeBytes);
    }

    public async Task DeleteAsync(ImageCategory category, string blobKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobKey)) return;
        var container = await GetContainerAsync(category, cancellationToken);
        await container.GetBlobClient(blobKey).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public string GetReadUrl(ImageCategory category, string blobKey)
    {
        EnsureConfigured();
        var blob = _client!.GetBlobContainerClient(ContainerName(category)).GetBlobClient(blobKey);
        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException(
                "Blob storage must be configured with an account key to generate read SAS URLs.");
        var builder = new BlobSasBuilder
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.ReadSasExpiryMinutes))
        };
        builder.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(builder).ToString();
    }

    private async Task<BlobContainerClient> GetContainerAsync(
        ImageCategory category, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var container = _client!.GetBlobContainerClient(ContainerName(category));
        // Private access only; never PublicAccessType.Blob/Container.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return container;
    }

    private void EnsureConfigured()
    {
        if (_client is null)
            throw new InvalidOperationException("FSCM blob storage is not configured (FscmBlobStorage:ConnectionString).");
    }

    private string ContainerName(ImageCategory category) => category switch
    {
        ImageCategory.ClaimEvidence => _options.ClaimEvidenceContainer,
        ImageCategory.WriteOffAttachment => _options.WriteOffAttachmentsContainer,
        ImageCategory.RetailerStorefront => _options.RetailerStorefrontContainer,
        ImageCategory.ProductImage => _options.ProductImagesContainer,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };

    private static string CategoryPrefix(ImageCategory category) => category switch
    {
        ImageCategory.ClaimEvidence => "claim",
        ImageCategory.WriteOffAttachment => "write-off",
        ImageCategory.RetailerStorefront => "retailer",
        ImageCategory.ProductImage => "product",
        _ => "misc"
    };
}
