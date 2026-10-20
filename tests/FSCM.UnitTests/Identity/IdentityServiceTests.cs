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
            [Guid.Parse("15000000-0000-0000-0000-000000000001")],
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
                IdentityProviders.Local,
                Guid.NewGuid(),
                CancellationToken.None));
        var promoteException = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.UpdateRoleAsync(
                user.Id,
                SystemRoles.Admin,
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("forbidden", createException.Code);
        Assert.Equal("forbidden", promoteException.Code);
        Assert.Single(fixture.Store.Users);
    }

    [Fact]
    public async Task Entra_exchange_links_only_preprovisioned_entra_account_and_issues_internal_tokens()
    {
        var fixture = new IdentityFixture();
        var user = fixture.AddActiveUser(
            SystemRoles.Operator,
            "portal@fscm.local",
            "Correct#Pass1",
            IdentityProviders.Entra);
        user.EntraObjectId = null;
        fixture.Store.Credentials.RemoveAll(credential => credential.UserAccountId == user.Id);

        var result = await fixture.Service.ExchangeEntraAsync(
            new EntraIdentity("entra-object-123", "PORTAL@FSCM.LOCAL"),
            "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal("entra-object-123", user.EntraObjectId);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Single(fixture.Store.RefreshTokens);
        Assert.Contains(fixture.Store.AuditLogs, audit => audit.Action == "EntraIdentityLinked");

        var refreshed = await fixture.Service.RefreshAsync(
            result.RefreshToken,
            "127.0.0.1",
            CancellationToken.None);
        Assert.Equal(user.Id, refreshed.User.Id);
    }

    [Fact]
    public async Task Entra_exchange_rejects_local_account_with_matching_email()
    {
        var fixture = new IdentityFixture();
        fixture.AddActiveUser(SystemRoles.Sales, "mobile@fscm.local", "Correct#Pass1");

        var exception = await Assert.ThrowsAsync<IdentityOperationException>(() =>
            fixture.Service.ExchangeEntraAsync(
                new EntraIdentity("entra-object-456", "mobile@fscm.local"),
                null,
                CancellationToken.None));

        Assert.Equal("forbidden", exception.Code);
        Assert.Empty(fixture.Store.RefreshTokens);
    }

    [Fact]
    public async Task User_list_is_filtered_and_paged_deterministically()
    {
        var fixture = new IdentityFixture();
        fixture.AddActiveUser(SystemRoles.Operator, "operator2@fscm.local", "Correct#Pass1");
        fixture.AddActiveUser(SystemRoles.Sales, "sales2@fscm.local", "Correct#Pass1");

        var result = await fixture.Service.ListUsersAsync(
            new UserListQuery(null, SystemRoles.Sales, true, 1, 20),
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(SystemRoles.Sales, result.Items.Single().Role);
        Assert.Equal(1, result.TotalCount);
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

        public UserAccount AddActiveUser(
            string role,
            string email,
            string password,
            string identityProvider = IdentityProviders.Local)
        {
            var user = new UserAccount
            {
                Id = Guid.NewGuid(),
                IdentityProvider = identityProvider,
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
        public List<UserRegionAssignment> RegionAssignments { get; } = [];
        public List<Retailer> Retailers { get; } = [];
        public List<AuditLog> AuditLogs { get; } = [];
        public HashSet<Guid> Warehouses { get; } = [];
        public HashSet<Guid> SalesGroups { get; } = [];
        public HashSet<Guid> Regions { get; } = [];
        public Dictionary<Guid, IdentityScope> Scopes { get; } = [];

        public Task<UserAccount?> FindUserByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Email == normalizedEmail));

        public Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<UserAccount?> FindUserByEntraObjectIdAsync(
            string entraObjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.EntraObjectId == entraObjectId));

        public Task<(IReadOnlyCollection<UserAccount> Items, int TotalCount)> ListUsersAsync(
            UserListQuery query,
            CancellationToken cancellationToken)
        {
            var users = Users.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query.Search))
                users = users.Where(user => user.Email.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
                    user.FullName.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(query.Role))
                users = users.Where(user => user.Role == query.Role);
            if (query.IsActive.HasValue)
                users = users.Where(user => user.IsActive == query.IsActive.Value);
            var ordered = users.OrderBy(user => user.FullName).ThenBy(user => user.Email).ToArray();
            return Task.FromResult<(IReadOnlyCollection<UserAccount>, int)>(
                (ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), ordered.Length));
        }

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

        public Task<bool> RegionExistsAsync(Guid regionId, CancellationToken cancellationToken) =>
            Task.FromResult(Regions.Contains(regionId));

        public Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.SingleOrDefault(retailer => retailer.Id == retailerId));

        public Task<Retailer?> FindRetailerByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.SingleOrDefault(retailer => retailer.UserAccountId == userId));

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
                membership => membership.UserAccountId == userId &&
                    membership.SalesGroupId == salesGroupId &&
                    membership.IsActive));

        public Task<IReadOnlyCollection<SalesGroupMember>> ListActiveSalesGroupMembershipsAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SalesGroupMember>>(SalesGroupMemberships
                .Where(membership => membership.UserAccountId == userId && membership.IsActive)
                .ToArray());

        public Task<UserRegionAssignment?> FindRegionAssignmentAsync(
            Guid userId,
            Guid regionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(RegionAssignments.SingleOrDefault(
                assignment => assignment.UserAccountId == userId && assignment.RegionId == regionId));

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
        public void AddRegionAssignment(UserRegionAssignment assignment) => RegionAssignments.Add(assignment);
        public void AddAuditLog(AuditLog auditLog) => AuditLogs.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
