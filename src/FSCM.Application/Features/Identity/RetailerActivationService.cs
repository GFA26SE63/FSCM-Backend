using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Identity;

public sealed class RetailerActivationService(
    IRetailerActivationStore store,
    ITransactionalEmailSender emailSender,
    ISmsSender smsSender,
    RetailerActivationOptions options,
    IIdentityTokenProvider tokenProvider,
    IPasswordHasher passwordHasher,
    IdentityService identityService,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SetupTokenLifetime = TimeSpan.FromMinutes(10);

    public Task<OtpRequestResult> RequestOtpAsync(string phone, string? ipAddress, CancellationToken cancellationToken) =>
        RequestOtpAsync(phone, null, ipAddress, cancellationToken);

    public async Task<OtpRequestResult> RequestOtpAsync(
        string phone, string? channel, string? ipAddress, CancellationToken cancellationToken)
    {
        var requestedChannel = OtpChannels.Normalize(channel)
            ?? OtpChannels.Normalize(options.DefaultOtpChannel)
            ?? OtpChannels.Email;
        phone = NormalizePhone(phone);
        var user = await store.FindUserByPhoneAsync(phone, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Không tìm thấy tài khoản Retailer với số điện thoại này.");
        if (user.Role != SystemRoles.Retailer)
            throw IdentityOperationException.Forbidden("Chỉ tài khoản Retailer được kích hoạt qua OTP.");
        if (user.IsActive)
            throw IdentityOperationException.Conflict("Tài khoản đã được kích hoạt.");

        var retailer = await store.FindRetailerByUserIdAsync(user.Id, cancellationToken)
            ?? throw IdentityOperationException.Validation("Tài khoản chưa được liên kết với Retailer.");
        // Email is mandatory for the Email channel and optional for SMS (used only as a fallback).
        var email = TryGetTrustedEmail(user);
        if (requestedChannel == OtpChannels.Email && email is null)
            throw IdentityOperationException.Validation("Retailer chưa có email hợp lệ và được liên kết đáng tin cậy.");
        var now = timeProvider.GetUtcNow();
        var latest = await store.FindLatestAsync(user.Id, cancellationToken);
        if (latest is not null && latest.ConsumedAt is null && latest.CreatedAt.Add(ResendCooldown) > now)
            throw IdentityOperationException.RateLimited("Vui lòng chờ 60 giây trước khi gửi lại OTP.");
        if (await store.CountRequestsForAccountAsync(user.Id, now.AddHours(-1), cancellationToken) >= 5)
            throw IdentityOperationException.RateLimited("Đã vượt giới hạn gửi OTP theo tài khoản. Vui lòng thử lại sau.");
        var limitedIp = LimitIp(ipAddress);
        if (limitedIp is not null && await store.CountRequestsForIpAsync(limitedIp, now.AddHours(-1), cancellationToken) >= 20)
            throw IdentityOperationException.RateLimited("Đã vượt giới hạn gửi OTP từ địa chỉ mạng này.");

        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var challenge = new RetailerActivationChallenge
        {
            Id = Guid.NewGuid(), UserAccountId = user.Id,
            OtpHash = HashOtp(user.Id, otp), RequestedIpAddress = limitedIp,
            FailedAttemptCount = 0, CreatedAt = now, ExpiresAt = now.Add(OtpLifetime)
        };
        store.AddChallenge(challenge);
        await store.SaveChangesAsync(cancellationToken);
        string deliveredChannel;
        try
        {
            deliveredChannel = await DeliverOtpAsync(requestedChannel, user, phone, email, otp, cancellationToken);
        }
        catch (IdentityOperationException)
        {
            // An undelivered OTP must not be usable, and must not block an immediate retry.
            challenge.ConsumedAt = now;
            await store.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return deliveredChannel == OtpChannels.Sms
            ? new OtpRequestResult(OtpChannels.Sms, MaskPhone(phone), null, challenge.ExpiresAt, now.Add(ResendCooldown))
            : new OtpRequestResult(OtpChannels.Email, MaskEmail(email!), MaskEmail(email!), challenge.ExpiresAt, now.Add(ResendCooldown));
    }

    private async Task<string> DeliverOtpAsync(
        string channel, UserAccount user, string phone, string? email, string otp,
        CancellationToken cancellationToken)
    {
        if (channel == OtpChannels.Sms)
        {
            try
            {
                await smsSender.SendRetailerActivationOtpAsync(phone, otp, cancellationToken);
                return OtpChannels.Sms;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (!options.FallbackToEmail || email is null)
                    throw IdentityOperationException.SmsDeliveryFailed();
            }
        }

        try
        {
            await emailSender.SendRetailerActivationOtpAsync(email!, user.FullName, otp, cancellationToken);
            return OtpChannels.Email;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw channel == OtpChannels.Sms
                ? IdentityOperationException.SmsDeliveryFailed()
                : IdentityOperationException.EmailDeliveryFailed();
        }
    }

    public async Task<OtpVerificationResult> VerifyOtpAsync(string phone, string code, CancellationToken cancellationToken)
    {
        phone = NormalizePhone(phone);
        if (code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
            throw IdentityOperationException.InvalidOtp();
        var user = await store.FindUserByPhoneAsync(phone, cancellationToken)
            ?? throw IdentityOperationException.InvalidOtp();
        var challenge = await store.FindLatestAsync(user.Id, cancellationToken)
            ?? throw IdentityOperationException.InvalidOtp();
        var now = timeProvider.GetUtcNow();
        if (challenge.ConsumedAt is not null || challenge.VerifiedAt is not null || challenge.ExpiresAt <= now)
            throw IdentityOperationException.OtpExpired();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(challenge.OtpHash), Convert.FromHexString(HashOtp(user.Id, code))))
        {
            challenge.FailedAttemptCount++;
            if (challenge.FailedAttemptCount >= 5) challenge.ConsumedAt = now;
            await store.SaveChangesAsync(cancellationToken);
            throw challenge.FailedAttemptCount >= 5
                ? IdentityOperationException.OtpAttemptsExceeded()
                : IdentityOperationException.InvalidOtp(5 - challenge.FailedAttemptCount);
        }
        var setupToken = tokenProvider.GenerateOpaqueToken();
        challenge.VerifiedAt = now;
        challenge.SetupTokenHash = tokenProvider.HashOpaqueToken(setupToken);
        challenge.SetupTokenExpiresAt = now.Add(SetupTokenLifetime);
        await store.SaveChangesAsync(cancellationToken);
        return new OtpVerificationResult(setupToken, challenge.SetupTokenExpiresAt.Value);
    }

    public async Task<AuthenticationResult> SetPasswordAsync(string setupToken, string password, string? ipAddress, CancellationToken cancellationToken)
    {
        var challenge = await store.FindBySetupTokenHashAsync(tokenProvider.HashOpaqueToken(setupToken), cancellationToken)
            ?? throw IdentityOperationException.InvalidToken();
        var now = timeProvider.GetUtcNow();
        if (challenge.ConsumedAt is not null || challenge.VerifiedAt is null || challenge.SetupTokenExpiresAt <= now)
            throw IdentityOperationException.InvalidToken();
        var user = await store.FindUserByIdAsync(challenge.UserAccountId, cancellationToken)
            ?? throw IdentityOperationException.InvalidToken();
        if (user.IsActive) throw IdentityOperationException.Conflict("Tài khoản đã được kích hoạt.");
        IdentityService.ValidatePasswordForActivation(password);
        var credential = await store.FindCredentialAsync(user.Id, cancellationToken);
        if (credential is null)
        {
            credential = new UserCredential { Id = Guid.NewGuid(), UserAccountId = user.Id, CreatedAt = now };
            store.AddCredential(credential);
        }
        credential.PasswordHash = passwordHasher.Hash(password);
        credential.FailedLoginCount = 0;
        credential.LockoutEndAt = null;
        credential.MustChangePassword = false;
        credential.PasswordChangedAt = now;
        credential.UpdatedAt = now;
        user.IsActive = true;
        user.UpdatedAt = now;
        challenge.ConsumedAt = now;
        await store.SaveChangesAsync(cancellationToken);
        return await identityService.LoginAsync(user.Phone!, password, ipAddress, cancellationToken);
    }

    private string HashOtp(Guid userId, string otp) => tokenProvider.HashOpaqueToken($"{userId:N}:{otp}");
    private static string NormalizePhone(string phone)
    {
        var normalized = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (normalized.Length != 10 || normalized[0] != '0')
            throw IdentityOperationException.Validation("Số điện thoại không hợp lệ.");
        return normalized;
    }
    private static string? TryGetTrustedEmail(UserAccount user)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return null;
        try { return new MailAddress(user.Email.Trim()).Address; }
        catch (FormatException) { return null; }
    }
    private static string MaskPhone(string phone) => $"{phone[..3]}****{phone[^3..]}";
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        var local = email[..at];
        return $"{local[0]}{new string('*', Math.Max(2, local.Length - 1))}{email[at..]}";
    }
    private static string? LimitIp(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(64, value.Trim().Length)];
}
