using System.Net.Http.Json;
using FSCM.Application.Features.Identity;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.ExternalServices;

/// <summary>
/// Sends transactional SMS through Brevo (<c>POST /v3/transactionalSMS/send</c>).
/// Brevo SMS is prepaid: the account needs SMS credits, otherwise Brevo rejects the request.
/// </summary>
public sealed class BrevoSmsSender(HttpClient client, IOptions<BrevoOptions> options) : ISmsSender
{
    public async Task SendRetailerActivationOtpAsync(string phone, string otp, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SmsSender))
            throw new InvalidOperationException("Brevo transactional SMS is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "transactionalSMS/send");
        request.Headers.Add("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            sender = settings.SmsSender,
            recipient = ToInternational(phone, settings.SmsCountryCode),
            // ASCII (no Vietnamese diacritics) keeps the message in one 160-character GSM segment.
            // Do not add a STOP code: Brevo would reclassify the message as marketing.
            content = $"FSCM: Ma OTP kich hoat tai khoan cua ban la {otp}. " +
                      "Ma co hieu luc 5 phut. Khong chia se ma nay cho bat ky ai.",
            type = "transactional",
            tag = "retailer-activation-otp",
            unicodeEnabled = false
        });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Brevo SMS returned HTTP {(int)response.StatusCode}.");
    }

    internal static string ToInternational(string phone, string countryCode)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        var code = new string(countryCode.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith('0')) return code + digits[1..];
        return digits.StartsWith(code, StringComparison.Ordinal) ? digits : code + digits;
    }
}
