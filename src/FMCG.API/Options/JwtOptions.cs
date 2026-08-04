namespace FMCG.API.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string Algorithm { get; set; } = "HS256";

    public int AccessTokenExpiryMinutes { get; set; } = 60;

    public int RefreshTokenExpiryDays { get; set; } = 30;
}
