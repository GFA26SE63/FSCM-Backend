namespace FSCM.Infrastructure.ExternalServices;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";
    public string ApiBaseUrl { get; set; } = "https://api.brevo.com/v3/";
    public string ApiKey { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = "FSCM";

    /// <summary>
    /// SMS sender name: up to 11 letters/digits (alphanumeric) or up to 15 digits (numeric).
    /// Alphanumeric senders in Vietnam usually need a registered brand name.
    /// </summary>
    public string SmsSender { get; set; } = "FSCM";

    /// <summary>Country calling code used to convert local numbers (0xxxxxxxxx) to international format.</summary>
    public string SmsCountryCode { get; set; } = "84";
}
