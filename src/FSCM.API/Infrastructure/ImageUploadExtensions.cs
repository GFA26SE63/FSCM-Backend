using FSCM.Application.Features.Storage;

namespace FSCM.API.Infrastructure;

public static class ImageUploadLimits
{
    /// <summary>Request body cap for image uploads: image max + small multipart overhead.</summary>
    public const long MaxRequestBytes = ImageValidation.MaxBytes + (256 * 1024);
}

public static class ImageUploadExtensions
{
    /// <summary>
    /// Converts a multipart <see cref="IFormFile"/> into an <see cref="ImageUpload"/> the Application
    /// layer understands, without leaking ASP.NET types past the controller. The returned stream is
    /// disposable; dispose it after the service call.
    /// </summary>
    public static async Task<DisposableImageUpload> ToImageUploadAsync(
        this IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw ImageStorageException.Validation("Vui lòng chọn một tệp ảnh.");

        var stream = file.OpenReadStream();
        var upload = new ImageUpload(stream, file.FileName, file.ContentType ?? string.Empty, file.Length);
        return await Task.FromResult(new DisposableImageUpload(upload, stream));
    }
}

/// <summary>Wraps an <see cref="ImageUpload"/> plus its underlying stream for <c>await using</c>.</summary>
public sealed class DisposableImageUpload(ImageUpload upload, Stream stream) : IAsyncDisposable
{
    public ImageUpload Upload { get; } = upload;

    public static implicit operator ImageUpload(DisposableImageUpload value) => value.Upload;

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
