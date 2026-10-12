using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Identity;

public sealed class IdentityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_issues_tokens_and_includes_current_scope()
    {
        var fixture = new IdentityFixture();
        var user = fixture.AddActiveUser(SystemRoles.Sales, "sales@fscm.local", "Correct#Pass1");
        fixture.Store.Scopes[user.Id] = new IdentityScope(
            [Guid.Parse("10000000-0000-0000-0000-000000000001")],
            [Guid.Parse("20000000-0000-0000-0000-000000000001")],
            [Guid.Parse("30000000-0000-0000-0000-000000000001")]);

        var result = await fixture.Service.LoginAsync(
            "SALES@FSCM.LOCAL",
            "Correct#Pass1",
            "127.0.0.1",
            CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Single(result.User.Scope.WarehouseIds);
        Assert.Single(fixture.Store.RefreshTokens);
        Assert.Equal("hash:opaque-token-1", fixture.Store.RefreshTokens[0].TokenHash);
    }

    [Fact]
    public async Task Login_locks_account_after_five_failed_attempts()
    {
        var fixture = new IdentityFixture();
        var user = fixture.AddActiveUser(SystemRoles.Operator, "operator@fscm.local", "Correct#Pass1");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var exception = await Assert.ThrowsAsync<IdentityOperationException>(() =>
                fixture.Service.LoginAsync(
                    user.Email,
                    "Wrong#Pass12",
                    null,
                    CancellationToken.None));
            Assert.Equal("invalid_credentials", exception.Code);
        }

        var lockedException = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.LoginAsync(
                user.Email,
                "Correct#Pass1",
                null,
                CancellationToken.None));

        Assert.Equal("account_locked", lockedException.Code);
        Assert.Equal(Now.AddMinutes(15), fixture.Store.Credentials.Single().LockoutEndAt);
    }

    [Fact]
    public async Task Refresh_rotates_the_refresh_token()
    {
        var fixture = new IdentityFixture();
        var user = fixture.AddActiveUser(SystemRoles.Retailer, "retailer@fscm.local", "Correct#Pass1");
        fixture.Store.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = user.Id,
            TokenHash = "hash:existing-token",
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(1)
        });

        var result = await fixture.Service.RefreshAsync(
            "existing-token",
            "127.0.0.1",
            CancellationToken.None);

        var oldToken = fixture.Store.RefreshTokens.Single(token => token.TokenHash == "hash:existing-token");
        Assert.Equal(Now, oldToken.RevokedAt);
        Assert.NotNull(oldToken.ReplacedByTokenId);
        Assert.Equal("opaque-token-1", result.RefreshToken);
        Assert.Equal(2, fixture.Store.RefreshTokens.Count);
    }

    [Fact]
    public async Task User_management_never_creates_or_promotes_admin()
    {
        var fixture = new IdentityFixture();
        var user = fixture.AddActiveUser(SystemRoles.Sales, "sales@fscm.local", "Correct#Pass1");

        var createException = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.CreateUserAsync(
                "another@fscm.local",
                "Another User",
                SystemRoles.Admin,
                CancellationToken.None));
        var promoteException = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.UpdateRoleAsync(
                user.Id,
                SystemRoles.Admin,
                CancellationToken.None));

        Assert.Equal("forbidden", createException.Code);
        Assert.Equal("forbidden", promoteException.Code);
        Assert.Single(fixture.Store.Users);
    }

    private sealed class IdentityFixture
    {
        public IdentityFixture()
        {
            Store = new InMemoryIdentityStore();
            PasswordHasher = new TestPasswordHasher();
            Service = new IdentityService(
                Store,
                PasswordHasher,
                new TestTokenProvider(),
                new TestTimeProvider(Now));
        }

        public InMemoryIdentityStore Store { get; }

        public TestPasswordHasher PasswordHasher { get; }

        public IdentityService Service { get; }

        public UserAccount AddActiveUser(string role, string email, string password)
        {
            var user = new UserAccount
            {
                Id = Guid.NewGuid(),
                EntraObjectId = $"local:{Guid.NewGuid():N}",
                Email = email,
                FullName = "Test User",
                Role = role,
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            Store.Users.Add(user);
            Store.Credentials.Add(new UserCredential
            {
                Id = Guid.NewGuid(),
                UserAccountId = user.Id,
                PasswordHash = PasswordHasher.Hash(password),
                CreatedAt = Now,
                UpdatedAt = Now
            });
            return user;
        }
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";

        public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
    }

    private sealed class TestTokenProvider : IIdentityTokenProvider
    {
        private int sequence;

        public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(30);

        public string GenerateOpaqueToken() => $"opaque-token-{++sequence}";

        public string HashOpaqueToken(string token) => $"hash:{token}";

        public IssuedAccessToken CreateAccessToken(
            UserAccount user,
            IdentityScope scope,
            DateTimeOffset issuedAt) =>
            new("access-token", issuedAt.AddHours(1));
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryIdentityStore : IIdentityStore
    {
        public List<UserAccount> Users { get; } = [];
        public List<UserCredential> Credentials { get; } = [];
        public List<RefreshToken> RefreshTokens { get; } = [];
        public List<AccountToken> AccountTokens { get; } = [];
        public List<UserWarehouseAssignment> WarehouseAssignments { get; } = [];
        public List<SalesGroupMember> SalesGroupMemberships { get; } = [];
        public HashSet<Guid> Warehouses { get; } = [];
        public HashSet<Guid> SalesGroups { get; } = [];
        public Dictionary<Guid, IdentityScope> Scopes { get; } = [];

        public Task<UserAccount?> FindUserByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Email == normalizedEmail));

        public Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Credentials.SingleOrDefault(credential => credential.UserAccountId == userId));

        public Task<RefreshToken?> FindRefreshTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult(RefreshTokens.SingleOrDefault(token => token.TokenHash == tokenHash));

        public Task<AccountToken?> FindAccountTokenAsync(
            string tokenHash,
            string purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult(AccountTokens.SingleOrDefault(
                token => token.TokenHash == tokenHash && token.Purpose == purpose));

        public Task<IdentityScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Scopes.GetValueOrDefault(userId, IdentityScope.Empty));

        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(Warehouses.Contains(warehouseId));

        public Task<bool> SalesGroupExistsAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
            Task.FromResult(SalesGroups.Contains(salesGroupId));

        public Task<UserWarehouseAssignment?> FindWarehouseAssignmentAsync(
            Guid userId,
            Guid warehouseId,
            CancellationToken cancellationToken) =>
            Task.FromResult(WarehouseAssignments.SingleOrDefault(
                assignment => assignment.UserAccountId == userId && assignment.WarehouseId == warehouseId));

        public Task<SalesGroupMember?> FindSalesGroupMembershipAsync(
            Guid userId,
            Guid salesGroupId,
            CancellationToken cancellationToken) =>
            Task.FromResult(SalesGroupMemberships.SingleOrDefault(
                membership => membership.UserAccountId == userId && membership.SalesGroupId == salesGroupId));

        public Task<IReadOnlyCollection<RefreshToken>> GetActiveRefreshTokensAsync(
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RefreshToken>>(RefreshTokens
                .Where(token => token.UserAccountId == userId && token.RevokedAt is null && token.ExpiresAt > now)
                .ToArray());

        public void AddUser(UserAccount user) => Users.Add(user);
        public void AddCredential(UserCredential credential) => Credentials.Add(credential);
        public void AddRefreshToken(RefreshToken refreshToken) => RefreshTokens.Add(refreshToken);
        public void AddAccountToken(AccountToken accountToken) => AccountTokens.Add(accountToken);
        public void AddWarehouseAssignment(UserWarehouseAssignment assignment) => WarehouseAssignments.Add(assignment);
        public void AddSalesGroupMembership(SalesGroupMember membership) => SalesGroupMemberships.Add(membership);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
