using System.Net.Http.Headers;
using FSCM.Application.Features.Identity;
using FSCM.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IIdentityTokenProvider, JwtIdentityTokenProvider>();

        services.AddHttpClient("OpenAI", client =>
        {
            var baseUrl = configuration["OpenAI:BaseUrl"];
            var apiKey = configuration["OpenAI:ApiKey"];

            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });

        return services;
    }
}
