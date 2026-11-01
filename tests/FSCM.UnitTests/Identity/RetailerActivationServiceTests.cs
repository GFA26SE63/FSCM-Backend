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

    private sealed class Fixture
    {
        public ActivationStore Store { get; } = new();
        public EmailSender Email { get; } = new();
        public RetailerActivationService Service { get; }

        public Fixture()
        {
            var user = new UserAccount
            {
                Id = Guid.NewGuid(), Email = "retailer@example.com", Phone = "0901234567",
                FullName = "Điểm bán An Phú", Role = SystemRoles.Retailer, IsActive = false
            };
            Store.Users.Add(user);
            Store.Retailers.Add(new Retailer { Id = Guid.NewGuid(), UserAccountId = user.Id });
            Service = new RetailerActivationService(Store, Email, new TokenProvider(), new PasswordHasher(), null!,
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
