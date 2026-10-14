namespace FSCM.Application.Features.Identity;

public sealed record IdentityScope(
    IReadOnlyCollection<Guid> WarehouseIds,
    IReadOnlyCollection<Guid> SalesGroupIds,
    IReadOnlyCollection<Guid> RetailerIds)
{
    public static IdentityScope Empty { get; } = new([], [], []);
}

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
    string ActivationToken,
    DateTimeOffset ActivationTokenExpiresAt);

