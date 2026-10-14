using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FSCM.Infrastructure.Identity;

public sealed class JwtIdentityTokenProvider : IIdentityTokenProvider
{
    private readonly string issuer;
    private readonly string audience;
    private readonly byte[] signingKey;
    private readonly string algorithm;
    private readonly TimeSpan accessTokenLifetime;

    public JwtIdentityTokenProvider(IConfiguration configuration)
    {
        issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer must be configured.");
        audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience must be configured.");
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret must be configured.");
        signingKey = Encoding.UTF8.GetBytes(secret);
        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Secret must contain at least 32 UTF-8 bytes.");
        }

        algorithm = ResolveAlgorithm(configuration["Jwt:Algorithm"]);
        accessTokenLifetime = TimeSpan.FromMinutes(
            ReadPositiveInt(configuration, "Jwt:AccessTokenExpiryMinutes", 60));
        RefreshTokenLifetime = TimeSpan.FromDays(
            ReadPositiveInt(configuration, "Jwt:RefreshTokenExpiryDays", 30));
    }

    public TimeSpan RefreshTokenLifetime { get; }

    public string GenerateOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public string HashOpaqueToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public IssuedAccessToken CreateAccessToken(
        UserAccount user,
        IdentityScope scope,
        DateTimeOffset issuedAt)
    {
        var expiresAt = issuedAt.Add(accessTokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(scope.WarehouseIds.Select(id => new Claim(ScopeClaimTypes.Warehouse, id.ToString())));
        claims.AddRange(scope.SalesGroupIds.Select(id => new Claim(ScopeClaimTypes.SalesGroup, id.ToString())));
        claims.AddRange(scope.RetailerIds.Select(id => new Claim(ScopeClaimTypes.Retailer, id.ToString())));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), algorithm)
        };
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new IssuedAccessToken(handler.WriteToken(token), expiresAt);
    }

    private static int ReadPositiveInt(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration.GetValue<int?>(key) ?? defaultValue;
        return value > 0
            ? value
            : throw new InvalidOperationException($"{key} must be greater than zero.");
    }

    private static string ResolveAlgorithm(string? configuredAlgorithm) => configuredAlgorithm switch
    {
        null or "HS256" => SecurityAlgorithms.HmacSha256,
        "HS384" => SecurityAlgorithms.HmacSha384,
        "HS512" => SecurityAlgorithms.HmacSha512,
        _ => throw new InvalidOperationException("Jwt:Algorithm must be HS256, HS384, or HS512.")
    };
}

