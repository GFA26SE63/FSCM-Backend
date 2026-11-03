namespace FSCM.Infrastructure.ExternalServices;

/// <summary>
/// Bound from the <c>FscmBlobStorage</c> configuration section. Holds the Azure Storage connection
/// string and the private container name per business image group.
/// </summary>
public sealed class FscmBlobStorageOptions
{
    public const string SectionName = "FscmBlobStorage";

    public string ConnectionString { get; set; } = string.Empty;

    public string ClaimEvidenceContainer { get; set; } = "claim-evidence";
    public string WriteOffAttachmentsContainer { get; set; } = "write-off-attachments";
    public string RetailerStorefrontContainer { get; set; } = "retailer-storefront-images";
    public string ProductImagesContainer { get; set; } = "product-images";

    /// <summary>Lifetime of the read-only SAS URLs handed to clients.</summary>
    public int ReadSasExpiryMinutes { get; set; } = 60;
}
