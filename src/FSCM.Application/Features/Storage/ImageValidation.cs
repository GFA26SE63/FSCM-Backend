namespace FSCM.Application.Features.Storage;

/// <summary>
/// Shared image rules. Only JPG/JPEG/PNG/WebP are accepted in this phase (no PDF/Office). The
/// content type is cross-checked against the magic-number signature so a renamed file is rejected.
/// </summary>
public static class ImageValidation
{
    public const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && ExtensionByContentType.ContainsKey(contentType.Trim());

    public static string NormalizeContentType(string contentType) =>
        contentType.Trim().Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : contentType.Trim().ToLowerInvariant();

    public static string ExtensionFor(string contentType) =>
        ExtensionByContentType.TryGetValue(contentType.Trim(), out var ext) ? ext : ".bin";

    /// <summary>
    /// Verifies the first bytes match the declared image type (JPEG, PNG, or WebP/RIFF). Guards
    /// against a client sending, for example, an executable renamed to .png.
    /// </summary>
    public static bool SignatureMatches(ReadOnlySpan<byte> header, string contentType)
    {
        var normalized = NormalizeContentType(contentType);
        return normalized switch
        {
            "image/jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 &&
                           header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D &&
                           header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "image/webp" => header.Length >= 12 &&
                            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // "RIFF"
                            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50, // "WEBP"
            _ => false
        };
    }
}
