using FSCM.Application.Features.QrLabels;
using QRCoder;

namespace FSCM.Infrastructure.ExternalServices;

/// <summary>
/// QR renderer backed by the QRCoder library (MIT, pure managed code - no System.Drawing),
/// so it runs on Linux/containers. Used to produce scannable TEM label artifacts.
/// </summary>
public sealed class QrCoderQrCodeRenderer : IQrCodeRenderer
{
    public string RenderSvg(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("QR payload is required.", nameof(payload));
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(4);
    }

    public byte[] RenderPng(string payload, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("QR payload is required.", nameof(payload));
        if (pixelsPerModule is < 1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerModule));
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
