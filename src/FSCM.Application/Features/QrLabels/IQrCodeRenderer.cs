namespace FSCM.Application.Features.QrLabels;

/// <summary>Renders QR code artifacts for printable TEM labels (BE Sprint 1).</summary>
public interface IQrCodeRenderer
{
    /// <summary>Returns a standalone SVG document encoding <paramref name="payload"/> as a QR code.</summary>
    string RenderSvg(string payload);

    /// <summary>Returns a PNG image (raw bytes) encoding <paramref name="payload"/> as a QR code.</summary>
    byte[] RenderPng(string payload, int pixelsPerModule = 10);
}
