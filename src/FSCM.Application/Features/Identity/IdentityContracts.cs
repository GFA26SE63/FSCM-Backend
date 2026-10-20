namespace FSCM.Application.Features.Identity;

public sealed record IdentityScope(
    IReadOnlyCollection<Guid> WarehouseIds,
    IReadOnlyCollection<Guid> RegionIds,
    IReadOnlyCollection<Guid> SalesGroupIds,
    IReadOnlyCollection<Guid> RetailerIds)
{
    public static IdentityScope Empty { get; } = new([], [], [], []);
}

public static class IdentityProviders
{
    public const string Local = "Local";
    public const string Entra = "Entra";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Local,
        Entra
    };
}

public sealed record EntraIdentity(string ObjectId, string Email);

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
    IdentityScope Scope);

public sealed record CreatedUserResult(
    Guid UserId,
    string Email,
    string Role,
    string IdentityProvider,
    string? ActivationToken,
    DateTimeOffset? ActivationTokenExpiresAt);

