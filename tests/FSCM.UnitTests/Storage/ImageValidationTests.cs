using FSCM.Application.Features.Storage;

namespace FSCM.UnitTests.Storage;

public sealed class ImageValidationTests
{
    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/jpg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("application/pdf", false)]
    [InlineData("image/gif", false)]
    [InlineData(null, false)]
    public void Allowed_content_types_are_restricted_to_images(string? contentType, bool expected) =>
        Assert.Equal(expected, ImageValidation.IsAllowedContentType(contentType));

    [Fact]
    public void Jpg_content_type_normalizes_to_jpeg() =>
        Assert.Equal("image/jpeg", ImageValidation.NormalizeContentType("image/jpg"));

    [Fact]
    public void Png_signature_matches_png_header()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.True(ImageValidation.SignatureMatches(png, "image/png"));
        Assert.False(ImageValidation.SignatureMatches(png, "image/jpeg"));
    }

    [Fact]
    public void Webp_signature_requires_riff_and_webp_markers()
    {
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];
        Assert.True(ImageValidation.SignatureMatches(webp, "image/webp"));
    }

    [Fact]
    public async Task Upload_service_rejects_oversized_image()
    {
        var service = new FakeImageStorage().ToUploadService();
        using var big = new MemoryStream(new byte[ImageValidation.MaxBytes + 1]);
        var upload = new ImageUpload(big, "big.png", "image/png", big.Length);

        var error = await Assert.ThrowsAsync<ImageStorageException>(() =>
            service.ValidateAndUploadAsync(ImageCategory.ProductImage, upload, CancellationToken.None));
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task Upload_service_fails_when_storage_not_configured()
    {
        var storage = new FakeImageStorage { Configured = false };
        var service = storage.ToUploadService();
        var error = await Assert.ThrowsAsync<ImageStorageException>(() =>
            service.ValidateAndUploadAsync(ImageCategory.ProductImage, FakeImageFactory.Png(), CancellationToken.None));
        Assert.Equal("service_unavailable", error.Code);
    }
}
