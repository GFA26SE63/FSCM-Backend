using FSCM.Application.Features.Storage;

namespace FSCM.UnitTests.Storage;

/// <summary>
/// In-memory <see cref="IImageStorage"/> for unit tests. Records uploads/deletes and returns a
/// deterministic fake read URL so services can be exercised without Azure.
/// </summary>
public sealed class FakeImageStorage : IImageStorage
{
    public Dictionary<string, (ImageCategory Category, byte[] Content, string ContentType)> Blobs { get; } = new();
    public List<string> Deleted { get; } = [];
    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public async Task<StoredImage> UploadAsync(ImageCategory category, ImageUpload upload, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await upload.Content.CopyToAsync(ms, cancellationToken);
        var key = $"{category}/{Guid.NewGuid():N}";
        Blobs[key] = (category, ms.ToArray(), upload.ContentType);
        return new StoredImage(key, upload.FileName, upload.ContentType, upload.SizeBytes);
    }

    public Task DeleteAsync(ImageCategory category, string blobKey, CancellationToken cancellationToken)
    {
        Deleted.Add(blobKey);
        Blobs.Remove(blobKey);
        return Task.CompletedTask;
    }

    public string GetReadUrl(ImageCategory category, string blobKey) =>
        $"https://fake.blob/{category}/{blobKey}?sig=test";

    /// <summary>Convenience factory for an ImageUploadService wrapping this fake.</summary>
    public ImageUploadService ToUploadService() => new(this);
}

/// <summary>Helpers to build valid image uploads (correct magic numbers) in tests.</summary>
public static class FakeImageFactory
{
    public static ImageUpload Png(string fileName = "photo.png")
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03];
        var stream = new MemoryStream(header);
        return new ImageUpload(stream, fileName, "image/png", header.Length);
    }

    public static ImageUpload Jpeg(string fileName = "photo.jpg")
    {
        byte[] header = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
        var stream = new MemoryStream(header);
        return new ImageUpload(stream, fileName, "image/jpeg", header.Length);
    }

    public static ImageUpload Fake(string contentType)
    {
        byte[] body = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];
        var stream = new MemoryStream(body);
        return new ImageUpload(stream, "bad.bin", contentType, body.Length);
    }
}
