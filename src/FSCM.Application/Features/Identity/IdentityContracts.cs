namespace FSCM.Application.Features.Identity;

public sealed record IdentityScope(
    IReadOnlyCollection<Guid> WarehouseIds,
    IReadOnlyCollection<Guid> RegionIds,
    IReadOnlyCollection<Guid> SalesGroupIds,
    IReadOnlyCollection<Guid> RetailerIds)
{
    public static IdentityScope Empty { get; } = new([], [], [], []);
}

/// <summary>A scoped entity reference with its code and display name (F08 /me labels).</summary>
public sealed record ScopeLabel(Guid Id, string Code, string Name);

/// <summary>Human-readable labels for the current user's scope, returned alongside the ID scope.</summary>
public sealed record IdentityScopeLabels(
    IReadOnlyCollection<ScopeLabel> Warehouses,
    IReadOnlyCollection<ScopeLabel> Regions,
    IReadOnlyCollection<ScopeLabel> SalesGroups,
    IReadOnlyCollection<ScopeLabel> Retailers)
{
    public static IdentityScopeLabels Empty { get; } = new([], [], [], []);
}

public static class IdentityProviders
{
    public const string Local = "Local";
    public const string Google = "Google";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Local,
        Google
    };
}

public sealed record GoogleIdentity(string Subject, string Email);

public sealed record UserListQuery(
    string? Search,
    string? Role,
    bool? IsActive,
    int Page,
    int PageSize);

public sealed record UserSummary(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    string IdentityProvider,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PagedUserResult(
    IReadOnlyCollection<UserSummary> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record AuthenticationResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedUser User);

public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool MustChangePassword,
    IdentityScope Scope,
    IdentityScopeLabels ScopeLabels);

public sealed record CreatedUserResult(
    Guid UserId,
    string Email,
    string Role,
    string IdentityProvider,
    string? ActivationToken,
    DateTimeOffset? ActivationTokenExpiresAt);

