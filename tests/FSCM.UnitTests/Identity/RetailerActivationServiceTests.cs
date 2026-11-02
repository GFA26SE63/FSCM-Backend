using System.Security.Cryptography;
using System.Text;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Identity;

public sealed class RetailerActivationServiceTests
{
    [Fact]
    public async Task Request_and_verify_use_hashed_single_use_otp()
    {
        var fixture = new Fixture();
        var requested = await fixture.Service.RequestOtpAsync("0901234567", "127.0.0.1", default);

        Assert.Contains("***", requested.MaskedEmail);
        Assert.NotEqual(fixture.Email.LastOtp, fixture.Store.Challenges.Single().OtpHash);
        var verified = await fixture.Service.VerifyOtpAsync("0901234567", fixture.Email.LastOtp!, default);
        Assert.False(string.IsNullOrWhiteSpace(verified.SetupToken));
        await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.VerifyOtpAsync("0901234567", fixture.Email.LastOtp!, default));
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_challenge()
    {
        var fixture = new Fixture();
        await fixture.Service.RequestOtpAsync("0901234567", null, default);
        var wrongOtp = fixture.Email.LastOtp == "000000" ? "999999" : "000000";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
                fixture.Service.VerifyOtpAsync("0901234567", wrongOtp, default));
            Assert.Equal("invalid_otp", error.Code);
        }
        var locked = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.VerifyOtpAsync("0901234567", wrongOtp, default));
        Assert.Equal("otp_attempts_exceeded", locked.Code);
        Assert.NotNull(fixture.Store.Challenges.Single().ConsumedAt);
    }

    [Fact]
    public async Task Resend_is_rejected_during_sixty_second_cooldown()
    {
        var fixture = new Fixture();
        await fixture.Service.RequestOtpAsync("0901234567", null, default);
        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.RequestOtpAsync("0901234567", null, default));
        Assert.Equal("rate_limited", error.Code);
    }

    [Fact]
    public async Task Default_channel_sends_otp_by_sms_with_masked_phone()
    {
        var fixture = new Fixture(defaultChannel: OtpChannels.Sms);

        var requested = await fixture.Service.RequestOtpAsync("0901234567", "127.0.0.1", default);

        Assert.Equal(OtpChannels.Sms, requested.Channel);
        Assert.Equal("090****567", requested.MaskedDestination);
        Assert.Null(requested.MaskedEmail);
        Assert.Equal("0901234567", fixture.Sms.LastPhone);
        Assert.Null(fixture.Email.LastOtp);
        var verified = await fixture.Service.VerifyOtpAsync("0901234567", fixture.Sms.LastOtp!, default);
        Assert.False(string.IsNullOrWhiteSpace(verified.SetupToken));
    }

    [Fact]
    public async Task Sms_failure_falls_back_to_email_when_enabled()
    {
        var fixture = new Fixture(defaultChannel: OtpChannels.Sms, fallbackToEmail: true);
        fixture.Sms.Fail = true;

        var requested = await fixture.Service.RequestOtpAsync("0901234567", null, default);

        Assert.Equal(OtpChannels.Email, requested.Channel);
        Assert.NotNull(fixture.Email.LastOtp);
        Assert.Null(fixture.Store.Challenges.Single().ConsumedAt);
    }

    [Fact]
    public async Task Sms_failure_without_fallback_consumes_challenge_and_allows_retry()
    {
        var fixture = new Fixture(defaultChannel: OtpChannels.Sms, fallbackToEmail: false);
        fixture.Sms.Fail = true;

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.RequestOtpAsync("0901234567", null, default));

        Assert.Equal("sms_delivery_failed", error.Code);
        Assert.NotNull(fixture.Store.Challenges.Single().ConsumedAt);
        fixture.Sms.Fail = false;
        var retried = await fixture.Service.RequestOtpAsync("0901234567", null, default);
        Assert.Equal(OtpChannels.Sms, retried.Channel);
    }

    [Fact]
    public async Task Sms_channel_does_not_require_email()
    {
        var fixture = new Fixture(defaultChannel: OtpChannels.Sms);
        fixture.Store.Users.Single().Email = "";

        var requested = await fixture.Service.RequestOtpAsync("0901234567", null, default);

        Assert.Equal(OtpChannels.Sms, requested.Channel);
    }

    [Fact]
    public async Task Invalid_channel_is_rejected()
    {
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.RequestOtpAsync("0901234567", "Zalo", null, default));

        Assert.Equal("validation_error", error.Code);
    }

    private sealed class Fixture
    {
        public ActivationStore Store { get; } = new();
        public EmailSender Email { get; } = new();
        public SmsSender Sms { get; } = new();
        public RetailerActivationService Service { get; }

        // Existing tests exercise the email channel; SMS tests pass defaultChannel explicitly.
        public Fixture(string defaultChannel = OtpChannels.Email, bool fallbackToEmail = true)
        {
            var user = new UserAccount
            {
                Id = Guid.NewGuid(), Email = "retailer@example.com", Phone = "0901234567",
                FullName = "Điểm bán An Phú", Role = SystemRoles.Retailer, IsActive = false
            };
            Store.Users.Add(user);
            Store.Retailers.Add(new Retailer { Id = Guid.NewGuid(), UserAccountId = user.Id });
            Service = new RetailerActivationService(Store, Email, Sms,
                new RetailerActivationOptions { DefaultOtpChannel = defaultChannel, FallbackToEmail = fallbackToEmail },
                new TokenProvider(), new PasswordHasher(), null!,
                new FixedTimeProvider(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero)));
        }
    }

    private sealed class ActivationStore : IRetailerActivationStore
    {
        public List<UserAccount> Users { get; } = [];
        public List<Retailer> Retailers { get; } = [];
        public List<UserCredential> Credentials { get; } = [];
        public List<RetailerActivationChallenge> Challenges { get; } = [];
        public Task<UserAccount?> FindUserByPhoneAsync(string phone, CancellationToken token) => Task.FromResult(Users.SingleOrDefault(x => x.Phone == phone));
        public Task<UserAccount?> FindUserByIdAsync(Guid id, CancellationToken token) => Task.FromResult(Users.SingleOrDefault(x => x.Id == id));
        public Task<UserCredential?> FindCredentialAsync(Guid id, CancellationToken token) => Task.FromResult(Credentials.SingleOrDefault(x => x.UserAccountId == id));
        public Task<Retailer?> FindRetailerByUserIdAsync(Guid id, CancellationToken token) => Task.FromResult(Retailers.SingleOrDefault(x => x.UserAccountId == id));
        public Task<RetailerActivationChallenge?> FindLatestAsync(Guid id, CancellationToken token) => Task.FromResult(Challenges.Where(x => x.UserAccountId == id).OrderByDescending(x => x.CreatedAt).FirstOrDefault());
        public Task<RetailerActivationChallenge?> FindBySetupTokenHashAsync(string hash, CancellationToken token) => Task.FromResult(Challenges.SingleOrDefault(x => x.SetupTokenHash == hash));
        public Task<int> CountRequestsForAccountAsync(Guid id, DateTimeOffset since, CancellationToken token) => Task.FromResult(Challenges.Count(x => x.UserAccountId == id && x.CreatedAt >= since));
        public Task<int> CountRequestsForIpAsync(string ip, DateTimeOffset since, CancellationToken token) => Task.FromResult(Challenges.Count(x => x.RequestedIpAddress == ip && x.CreatedAt >= since));
        public void AddChallenge(RetailerActivationChallenge challenge) => Challenges.Add(challenge);
        public void AddCredential(UserCredential credential) => Credentials.Add(credential);
        public Task SaveChangesAsync(CancellationToken token) => Task.CompletedTask;
    }

    private sealed class EmailSender : ITransactionalEmailSender
    {
        public string? LastOtp { get; private set; }
        public Task SendRetailerActivationOtpAsync(string email, string name, string otp, CancellationToken token) { LastOtp = otp; return Task.CompletedTask; }
        public Task SendRetailerAccountCreatedAsync(RetailerAccountCreatedEmail email, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class SmsSender : ISmsSender
    {
        public bool Fail { get; set; }
        public string? LastPhone { get; private set; }
        public string? LastOtp { get; private set; }
        public Task SendRetailerActivationOtpAsync(string phone, string otp, CancellationToken token)
        {
            if (Fail) throw new HttpRequestException("Brevo SMS returned HTTP 402.");
            LastPhone = phone;
            LastOtp = otp;
            return Task.CompletedTask;
        }
    }
    private sealed class TokenProvider : IIdentityTokenProvider
    {
        public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(1);
        public string GenerateOpaqueToken() => "setup-token";
        public string HashOpaqueToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        public IssuedAccessToken CreateAccessToken(UserAccount user, IdentityScope scope, DateTimeOffset issuedAt) => throw new NotSupportedException();
    }
    private sealed class PasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string passwordHash) => password == passwordHash;
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
