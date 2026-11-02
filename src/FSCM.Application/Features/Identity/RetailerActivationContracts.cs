using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Identity;

/// <summary>
/// Result of an OTP request. <see cref="Channel"/> is the channel the OTP was actually delivered on
/// (it can differ from the requested one when SMS falls back to email). <see cref="MaskedEmail"/>
/// is kept for older clients and is only set when the OTP went to email.
/// </summary>
public sealed record OtpRequestResult(
    string Channel,
    string MaskedDestination,
    string? MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt);

public static class OtpChannels
{
    public const string Sms = "Sms";
    public const string Email = "Email";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Equals(Sms, StringComparison.OrdinalIgnoreCase)) return Sms;
        if (value.Equals(Email, StringComparison.OrdinalIgnoreCase)) return Email;
        throw IdentityOperationException.Validation("Kênh OTP không hợp lệ. Chỉ hỗ trợ Sms hoặc Email.");
    }
}

/// <summary>Bound from the <c>RetailerActivation</c> configuration section.</summary>
public sealed class RetailerActivationOptions
{
    public const string SectionName = "RetailerActivation";

    /// <summary>Channel used when the client does not specify one: <c>Sms</c> or <c>Email</c>.</summary>
    public string DefaultOtpChannel { get; set; } = OtpChannels.Email;

    /// <summary>When SMS delivery fails, send the same OTP to the Retailer's email instead.</summary>
    public bool FallbackToEmail { get; set; } = true;
}

public interface ISmsSender
{
    /// <param name="phone">Vietnamese local phone number (10 digits starting with 0).</param>
    Task SendRetailerActivationOtpAsync(string phone, string otp, CancellationToken cancellationToken);
}
public sealed record OtpVerificationResult(string SetupToken, DateTimeOffset ExpiresAt);

public interface ITransactionalEmailSender
{
    Task SendRetailerActivationOtpAsync(string recipientEmail, string recipientName, string otp, CancellationToken cancellationToken);

    /// <summary>
    /// Notifies a Retailer that Admin approved the declaration and created the login account.
    /// The Retailer must sign in on Mobile with the phone number (empty password) and verify by OTP.
    /// </summary>
    Task SendRetailerAccountCreatedAsync(
        RetailerAccountCreatedEmail email, CancellationToken cancellationToken);
}

public sealed record RetailerAccountCreatedEmail(
    string RecipientEmail,
    string RecipientName,
    string RetailerName,
    string RetailerCode,
    string Phone);

public interface IRetailerActivationStore
{
    Task<UserAccount?> FindUserByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<Retailer?> FindRetailerByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<RetailerActivationChallenge?> FindLatestAsync(Guid userId, CancellationToken cancellationToken);
    Task<RetailerActivationChallenge?> FindBySetupTokenHashAsync(string hash, CancellationToken cancellationToken);
    Task<int> CountRequestsForAccountAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken);
    Task<int> CountRequestsForIpAsync(string ipAddress, DateTimeOffset since, CancellationToken cancellationToken);
    void AddChallenge(RetailerActivationChallenge challenge);
    void AddCredential(UserCredential credential);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
