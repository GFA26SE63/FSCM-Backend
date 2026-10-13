using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Identity;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}

public interface IIdentityTokenProvider
{
    string GenerateOpaqueToken();

    string HashOpaqueToken(string token);

    IssuedAccessToken CreateAccessToken(
        UserAccount user,
        IdentityScope scope,
        DateTimeOffset issuedAt);

    TimeSpan RefreshTokenLifetime { get; }
}

