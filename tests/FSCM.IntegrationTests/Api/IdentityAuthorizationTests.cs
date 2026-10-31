using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.EntityQueries;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FSCM.IntegrationTests.Api;

public sealed class IdentityAuthorizationTests : IClassFixture<IdentityAuthorizationTests.IdentityApiFactory>
{
    private readonly IdentityApiFactory factory;

    public IdentityAuthorizationTests(IdentityApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Users_endpoint_requires_authentication_role_and_current_active_account()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Sales));
        var salesResponse = await client.GetAsync("/api/v1/users");
        Assert.True(salesResponse.StatusCode == HttpStatusCode.Forbidden,
            $"Expected Forbidden but received {salesResponse.StatusCode}: " +
            string.Join(" | ", salesResponse.Headers.WwwAuthenticate));

        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.InactiveAdmin));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Admin));
        var response = await client.GetAsync("/api/v1/users?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(4, json.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GraphQl_legacy_root_queries_are_removed()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Admin));
        using var content = JsonContent.Create(new
        {
            query = "query { users(page: 1, pageSize: 20) { totalCount items { id email role isActive } } }"
        });

        var response = await client.PostAsync("/graphql", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(responseBody);
        Assert.True(json.RootElement.TryGetProperty("errors", out var errors), responseBody);
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task GraphQl_entities_support_projection_filter_sort_and_client_selected_paging()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Sales));
        using var content = JsonContent.Create(new
        {
            query = """
                query {
                  entities {
                    products(
                      first: 1
                      where: { sku: { startsWith: "SKU-GQL" } }
                      order: [{ sku: DESC }]) {
                      totalCount
                      nodes { id sku name }
                      pageInfo { hasNextPage endCursor }
                    }
                  }
                }
                """
        });

        var response = await client.PostAsync("/graphql", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);
        var json = JsonDocument.Parse(responseBody);
        Assert.False(json.RootElement.TryGetProperty("errors", out _), responseBody);
        var products = json.RootElement.GetProperty("data").GetProperty("entities").GetProperty("products");
        Assert.Equal(2, products.GetProperty("totalCount").GetInt32());
        Assert.Single(products.GetProperty("nodes").EnumerateArray());
        Assert.True(products.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(factory.Sales.Id, factory.EntityQueries.LastAccess?.UserId);
        Assert.Equal(SystemRoles.Sales, factory.EntityQueries.LastAccess?.Role);
    }

    [Fact]
    public async Task Territory_routes_and_graphql_enforce_authentication_role_and_guid_contracts()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/regions")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Sales));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/v1/regions", new { code = "Q12", name = "Quận 12", description = "Khu vực bán hàng" }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
            $"/api/v1/territory-assignments/sales/{Guid.NewGuid()}",
            new { destinationSalesGroupId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/sales-groups/not-a-guid")).StatusCode);

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { entities { regions(first: 1) { totalCount } salesGroups(first: 1) { totalCount } } }"
        });
        var graphQlBody = await graphQlResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        Assert.False(JsonDocument.Parse(graphQlBody).RootElement.TryGetProperty("errors", out _), graphQlBody);
    }

    [Fact]
    public async Task GraphQl_entities_require_an_authenticated_active_account()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { entities { products(first: 1) { nodes { id } } } }"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task Internal_transfer_public_contracts_are_disabled_for_the_registered_mvp()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Admin));

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/internal-transfers")).StatusCode);

        using var content = JsonContent.Create(new
        {
            query = "query { internalTransfers(first: 20) { totalCount } }"
        });
        var response = await client.PostAsync("/graphql", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task Graphql_exposes_shipment_receipt_actual_read_models()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(factory.CreateToken(factory.Admin));
        using var content = JsonContent.Create(new
        {
            query = "query { entities { shipments(first: 1) { totalCount } receiptConfirmations(first: 1) { totalCount nodes { shipmentId status } } receiptConfirmationItems(first: 1) { totalCount nodes { salesOrderLineId receivedQuantity damagedQuantity missingQuantity } } receiptConfirmationQrLabels(first: 1) { totalCount } } }"
        });

        var response = await client.PostAsync("/graphql", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Local_and_google_login_can_run_in_parallel_for_the_same_account()
    {
        using var client = factory.CreateClient();

        var localResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = factory.Admin.Email,
            Password = IdentityApiFactory.LocalPassword
        });
        Assert.Equal(HttpStatusCode.OK, localResponse.StatusCode);

        const string googleSubject = "google-subject-4c45f3a1";
        client.DefaultRequestHeaders.Authorization = Bearer(
            factory.CreateGoogleToken(factory.Admin, googleSubject));
        var googleResponse = await client.PostAsync("/api/v1/auth/google/exchange", null);
        Assert.Equal(HttpStatusCode.OK, googleResponse.StatusCode);
        Assert.Equal(googleSubject, factory.Admin.GoogleSubject);
        Assert.Equal(IdentityProviders.Local, factory.Admin.IdentityProvider);

        client.DefaultRequestHeaders.Authorization = null;
        var secondLocalResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = factory.Admin.Email,
            Password = IdentityApiFactory.LocalPassword
        });
        Assert.Equal(HttpStatusCode.OK, secondLocalResponse.StatusCode);
    }

    [Fact]
    public async Task Local_login_accepts_a_phone_identifier()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Identifier = factory.Admin.Phone,
            Password = IdentityApiFactory.LocalPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authentication = await response.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.Equal(factory.Admin.Id, authentication!.User.Id);
    }

    [Fact]
    public async Task Mobile_local_login_refresh_logout_and_inactive_account_lifecycle_is_enforced()
    {
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = factory.Admin.Email,
            Password = IdentityApiFactory.LocalPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(login);
        Assert.Equal(factory.Admin.Id, login.User.Id);
        Assert.Equal(SystemRoles.Admin, login.User.Role);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            login.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(refreshed);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        var reusedResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            login.RefreshToken
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reusedResponse.StatusCode);

        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new
        {
            refreshed.RefreshToken
        });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshed.RefreshToken })).StatusCode);

        var inactiveResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = factory.InactiveAdmin.Email,
            Password = IdentityApiFactory.LocalPassword
        });
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveResponse.StatusCode);
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    public sealed class IdentityApiFactory : WebApplicationFactory<Program>
    {
        private const string Issuer = "FSCM.API.Tests";
        private const string Audience = "FSCM.Clients.Tests";
        private const string GoogleIssuer = "https://accounts.google.com";
        private const string GoogleAudience = "fscm-tests-client.apps.googleusercontent.com";
        private const string Secret = "identity-tests-signing-key-at-least-32-bytes-long";
        public const string LocalPassword = "Correct#Pass1";
        private readonly TestIdentityStore store = new();

        public IdentityApiFactory()
        {
            Admin = AddUser("admin@test.local", SystemRoles.Admin, true);
            Sales = AddUser("sales@test.local", SystemRoles.Sales, true);
            WarehouseKeeper = AddUser("warehouse.keeper@test.local", SystemRoles.WarehouseKeeper, true);
            InactiveAdmin = AddUser("inactive.admin@test.local", SystemRoles.Admin, false);
            EntityQueries.Products.AddRange([
                new Product { Id = Guid.NewGuid(), Sku = "SKU-GQL-001", Name = "Sản phẩm GraphQL 1" },
                new Product { Id = Guid.NewGuid(), Sku = "SKU-GQL-002", Name = "Sản phẩm GraphQL 2" },
                new Product { Id = Guid.NewGuid(), Sku = "OTHER-001", Name = "Sản phẩm khác" }
            ]);
            store.Credentials.Add(new UserCredential
            {
                UserAccountId = Admin.Id,
                PasswordHash = TestPasswordHasher.CreateHash(LocalPassword),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            store.Credentials.Add(new UserCredential
            {
                UserAccountId = InactiveAdmin.Id,
                PasswordHash = TestPasswordHasher.CreateHash(LocalPassword),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        public UserAccount Admin { get; }
        public UserAccount Sales { get; }
        public UserAccount WarehouseKeeper { get; }
        public UserAccount InactiveAdmin { get; }
        public TestEntityQueryService EntityQueries { get; } = new();

        public UserAccount AddTestUser(string email, string role) => AddUser(email, role, true);

        public string CreateToken(UserAccount user)
        {
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim(ClaimTypes.Email, user.Email)
                ]),
                Expires = DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                    SecurityAlgorithms.HmacSha256)
            };
            var handler = new JwtSecurityTokenHandler();
            return handler.WriteToken(handler.CreateToken(descriptor));
        }

        public string CreateGoogleToken(UserAccount user, string googleSubject)
        {
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = GoogleIssuer,
                Audience = GoogleAudience,
                Subject = new ClaimsIdentity([
                    new Claim("sub", googleSubject),
                    new Claim("email", user.Email),
                    new Claim("email_verified", "true")
                ]),
                Expires = DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                    SecurityAlgorithms.HmacSha256)
            };
            var handler = new JwtSecurityTokenHandler();
            return handler.WriteToken(handler.CreateToken(descriptor));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Port=5432;Database=fscm_tests;Username=postgres;Password=test_only");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:Secret", Secret);
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "10");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "1");
            builder.UseSetting("BackgroundJobs:DateTracker:Enabled", "false");
            builder.UseSetting("BackgroundJobs:InventoryOutbox:Enabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityStore>();
                services.AddSingleton<IIdentityStore>(store);
                services.RemoveAll<IIdentityTokenProvider>();
                services.AddSingleton<IIdentityTokenProvider, TestIdentityTokenProvider>();
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher, TestPasswordHasher>();
                services.RemoveAll<IEntityQueryService>();
                services.AddSingleton<IEntityQueryService>(EntityQueries);
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options => options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = Issuer,
                        ValidateAudience = true,
                        ValidAudience = Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    });
                services.PostConfigure<JwtBearerOptions>(
                    FSCM.API.Security.AuthenticationSchemes.Google,
                    options =>
                    {
                        options.Authority = null;
                        options.Audience = GoogleAudience;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = GoogleIssuer,
                            ValidateAudience = true,
                            ValidAudience = GoogleAudience,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero
                        };
                    });
            });
        }

        private UserAccount AddUser(string email, string role, bool isActive)
        {
            var now = DateTimeOffset.UtcNow;
            var user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Email = email,
                Phone = $"09000000{store.Users.Count + 10:D2}",
                FullName = email,
                Role = role,
                IdentityProvider = IdentityProviders.Local,
                IsActive = isActive,
                CreatedAt = now,
                UpdatedAt = now
            };
            store.Users.Add(user);
            return user;
        }
    }

    public sealed class TestEntityQueryService : IEntityQueryService
    {
        public List<Product> Products { get; } = [];
        public EntityQueryAccess? LastAccess { get; private set; }

        public IQueryable<TEntity> Query<TEntity>(EntityQueryAccess access) where TEntity : class
        {
            LastAccess = access;
            if (typeof(TEntity) == typeof(Product))
                return (IQueryable<TEntity>)(object)Products.AsQueryable();
            return Array.Empty<TEntity>().AsQueryable();
        }
    }

    private sealed class TestIdentityTokenProvider : IIdentityTokenProvider
    {
        private int tokenSequence;
        public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(1);
        public string GenerateOpaqueToken() => $"opaque-test-token-{Interlocked.Increment(ref tokenSequence)}";
        public string HashOpaqueToken(string token) => $"hash:{token}";
        public IssuedAccessToken CreateAccessToken(
            UserAccount user,
            IdentityScope scope,
            DateTimeOffset issuedAt) => new("access-test-token", issuedAt.AddMinutes(10));
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public static string CreateHash(string password) => $"hash:{password}";

        public string Hash(string password) => CreateHash(password);

        public bool Verify(string password, string passwordHash) =>
            passwordHash == CreateHash(password);
    }

    private sealed class TestIdentityStore : IIdentityStore
    {
        public List<UserAccount> Users { get; } = [];
        public List<UserCredential> Credentials { get; } = [];
        public List<RefreshToken> RefreshTokens { get; } = [];

        public Task<UserAccount?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Email == normalizedEmail));
        public Task<UserAccount?> FindUserByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Phone == normalizedPhone));
        public Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));
        public Task<UserAccount?> FindUserByGoogleSubjectAsync(string googleSubject, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.GoogleSubject == googleSubject));
        public Task<(IReadOnlyCollection<UserAccount> Items, int TotalCount)> ListUsersAsync(
            UserListQuery query, CancellationToken cancellationToken)
        {
            var users = Users.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query.Role)) users = users.Where(user => user.Role == query.Role);
            if (query.IsActive.HasValue) users = users.Where(user => user.IsActive == query.IsActive.Value);
            var items = users.OrderBy(user => user.FullName).ThenBy(user => user.Id).ToArray();
            return Task.FromResult<(IReadOnlyCollection<UserAccount>, int)>(
                (items.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), items.Length));
        }
        public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Credentials.SingleOrDefault(credential => credential.UserAccountId == userId));
        public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(RefreshTokens.SingleOrDefault(token => token.TokenHash == tokenHash));
        public Task<AccountToken?> FindAccountTokenAsync(string tokenHash, string purpose, CancellationToken cancellationToken) => Task.FromResult<AccountToken?>(null);
        public Task<IdentityScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(IdentityScope.Empty);
        public Task<IdentityScopeLabels> GetScopeLabelsAsync(IdentityScope scope, CancellationToken cancellationToken) => Task.FromResult(IdentityScopeLabels.Empty);
        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> SalesGroupExistsAsync(Guid salesGroupId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> RegionExistsAsync(Guid regionId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<Retailer?>(null);
        public Task<Retailer?> FindRetailerByUserIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<Retailer?>(null);
        public Task<UserWarehouseAssignment?> FindWarehouseAssignmentAsync(Guid userId, Guid warehouseId, CancellationToken cancellationToken) => Task.FromResult<UserWarehouseAssignment?>(null);
        public Task<SalesGroupMember?> FindSalesGroupMembershipAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken) => Task.FromResult<SalesGroupMember?>(null);
        public Task<IReadOnlyCollection<SalesGroupMember>> ListActiveSalesGroupMembershipsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SalesGroupMember>>([]);
        public Task<UserRegionAssignment?> FindRegionAssignmentAsync(Guid userId, Guid regionId, CancellationToken cancellationToken) => Task.FromResult<UserRegionAssignment?>(null);
        public Task<IReadOnlyCollection<RefreshToken>> GetActiveRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RefreshToken>>(RefreshTokens.Where(token =>
                token.UserAccountId == userId && token.RevokedAt is null && token.ExpiresAt > now).ToArray());
        public void AddUser(UserAccount user) => Users.Add(user);
        public void AddCredential(UserCredential credential) => Credentials.Add(credential);
        public void AddRefreshToken(RefreshToken refreshToken) => RefreshTokens.Add(refreshToken);
        public void AddAccountToken(AccountToken accountToken) { }
        public void AddWarehouseAssignment(UserWarehouseAssignment assignment) { }
        public void AddSalesGroupMembership(SalesGroupMember membership) { }
        public void AddRegionAssignment(UserRegionAssignment assignment) { }
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
