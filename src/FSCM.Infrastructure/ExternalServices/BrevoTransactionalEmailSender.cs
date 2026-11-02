using System.Net.Http.Json;
using FSCM.Application.Features.Identity;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.ExternalServices;

public sealed class BrevoTransactionalEmailSender(HttpClient client, IOptions<BrevoOptions> options)
    : ITransactionalEmailSender
{
    public async Task SendRetailerActivationOtpAsync(
        string recipientEmail, string recipientName, string otp, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SenderEmail))
            throw new InvalidOperationException("Brevo transactional email is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "smtp/email");
        request.Headers.Add("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            sender = new { name = settings.SenderName, email = settings.SenderEmail },
            to = new[] { new { name = recipientName, email = recipientEmail } },
            subject = "Mã OTP kích hoạt tài khoản FSCM",
            htmlContent = $"""
                <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto">
                  <h2>Kích hoạt tài khoản FSCM</h2>
                  <p>Xin chào {System.Net.WebUtility.HtmlEncode(recipientName)},</p>
                  <p>Mã OTP của bạn là:</p>
                  <p style="font-size:32px;font-weight:700;letter-spacing:8px">{otp}</p>
                  <p>Mã có hiệu lực trong 5 phút và chỉ sử dụng được một lần.</p>
                  <p>Nếu bạn không yêu cầu mã này, vui lòng bỏ qua email.</p>
                </div>
                """
        });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Brevo returned HTTP {(int)response.StatusCode}.");
    }
}
