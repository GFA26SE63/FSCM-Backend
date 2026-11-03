using System.Net;
using System.Net.Http.Json;
using FSCM.Application.Features.Identity;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.ExternalServices;

public sealed class BrevoTransactionalEmailSender(HttpClient client, IOptions<BrevoOptions> options)
    : ITransactionalEmailSender
{
    public Task SendRetailerActivationOtpAsync(
        string recipientEmail, string recipientName, string otp, CancellationToken cancellationToken) =>
        SendAsync(recipientEmail, recipientName, "Mã OTP kích hoạt tài khoản FSCM", $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto">
              <h2>Kích hoạt tài khoản FSCM</h2>
              <p>Xin chào {WebUtility.HtmlEncode(recipientName)},</p>
              <p>Mã OTP của bạn là:</p>
              <p style="font-size:32px;font-weight:700;letter-spacing:8px">{otp}</p>
              <p>Mã có hiệu lực trong 5 phút và chỉ sử dụng được một lần.</p>
              <p>Nếu bạn không yêu cầu mã này, vui lòng bỏ qua email.</p>
            </div>
            """, cancellationToken);

    public Task SendRetailerAccountCreatedAsync(
        RetailerAccountCreatedEmail email, CancellationToken cancellationToken)
    {
        var name = WebUtility.HtmlEncode(email.RecipientName);
        var retailerName = WebUtility.HtmlEncode(email.RetailerName);
        var retailerCode = WebUtility.HtmlEncode(email.RetailerCode);
        var phone = WebUtility.HtmlEncode(email.Phone);
        return SendAsync(email.RecipientEmail, email.RecipientName,
            "Tài khoản FSCM của bạn đã được tạo", $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto">
              <h2>Tài khoản FSCM đã được tạo</h2>
              <p>Xin chào {name},</p>
              <p>Hồ sơ cửa hàng <strong>{retailerName}</strong> (mã <strong>{retailerCode}</strong>)
                 đã được phê duyệt và tài khoản đăng nhập của bạn đã được tạo.</p>
              <p>Vui lòng đăng nhập trên ứng dụng FSCM Mobile để xác minh tài khoản:</p>
              <ol>
                <li>Nhập số điện thoại <strong>{phone}</strong> và để trống mật khẩu.</li>
                <li>Ứng dụng sẽ gửi mã OTP tới email này (email tiêu đề "Mã OTP kích hoạt tài khoản FSCM"). Nhập mã trong vòng 5 phút.</li>
                <li>Đặt mật khẩu mới cho các lần đăng nhập sau.</li>
              </ol>
              <p>Nếu bạn không đăng ký tài khoản này, vui lòng bỏ qua email.</p>
            </div>
            """, cancellationToken);
    }

    private async Task SendAsync(
        string recipientEmail, string recipientName, string subject, string htmlContent,
        CancellationToken cancellationToken)
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
            subject,
            htmlContent
        });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Brevo returned HTTP {(int)response.StatusCode}.");
    }
}
