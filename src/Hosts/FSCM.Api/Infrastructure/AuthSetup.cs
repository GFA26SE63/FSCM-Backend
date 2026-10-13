using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;

namespace FSCM.Api.Infrastructure;

public static class AuthSetup
{
    public const string DevScheme = "FscmDev";

    /// <summary>
    /// Auth:Mode = "Entra" (default): JWT bearer issued by Microsoft Entra ID (section AzureAd).
    /// Auth:Mode = "Dev": no token, the caller sends header <c>X-Dev-User: &lt;FSCM user id&gt;</c> (e.g. the seeded "admin").
    /// The FSCM role is not taken from the token: CurrentUserMiddleware loads it from the Identity module.
    /// </summary>
    public static IServiceCollection AddFscmAuthentication(this IServiceCollection s, IConfiguration cfg)
    {
        if (string.Equals(cfg["Auth:Mode"], "Dev", StringComparison.OrdinalIgnoreCase))
        {
            s.AddAuthentication(DevScheme).AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevScheme, _ => { });
            return s;
        }
        s.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
         .AddMicrosoftIdentityWebApi(cfg.GetSection("AzureAd"));
        return s;
    }

    public static IServiceCollection AddFscmSwagger(this IServiceCollection s, IConfiguration cfg)
    {
        var dev = string.Equals(cfg["Auth:Mode"], "Dev", StringComparison.OrdinalIgnoreCase);
        s.AddEndpointsApiExplorer();
        s.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = "FSCM API", Version = "v1", Description = "FA26SE083 – FSCM modular monolith (GFA26SE63)" });
            o.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
            var scheme = dev
                ? new OpenApiSecurityScheme { Name = "X-Dev-User", In = ParameterLocation.Header, Type = SecuritySchemeType.ApiKey, Description = "Dev only: FSCM user id, e.g. admin" }
                : new OpenApiSecurityScheme { Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
            o.AddSecurityDefinition("fscm", scheme);
            o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "fscm" } }] = Array.Empty<string>() });
        });
        return s;
    }
}

/// <summary>Local development only. Never enable Auth:Mode=Dev on Azure.</summary>
public sealed class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Dev-User"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(user)) return Task.FromResult(AuthenticateResult.NoResult());
        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user) }, AuthSetup.DevScheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), AuthSetup.DevScheme)));
    }
}
