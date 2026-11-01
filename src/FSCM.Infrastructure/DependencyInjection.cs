using System.Net.Http.Headers;
using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Maps;
using FSCM.Application.Features.QrLabels;
using FSCM.Infrastructure.BackgroundJobs;
using FSCM.Infrastructure.ExternalServices;
using FSCM.Application.Features.Payments;
using FSCM.Infrastructure.Identity;
using FSCM.Infrastructure.Maps;
using FSCM.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddHttpClient();
        services.Configure<BrevoOptions>(configuration.GetSection(BrevoOptions.SectionName));
        services.AddHttpClient<ITransactionalEmailSender, BrevoTransactionalEmailSender>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<BrevoOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("GoogleMaps", client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IMapProvider, GoogleMapsProvider>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IIdentityTokenProvider, JwtIdentityTokenProvider>();
        services.AddSingleton<IQrCodeRenderer, QrCoderQrCodeRenderer>();

        services.Configure<DateTrackerOptions>(configuration.GetSection(DateTrackerOptions.SectionName));
        services.AddHostedService<DateTrackerHostedService>();
        services.Configure<AllocationRunOptions>(configuration.GetSection(AllocationRunOptions.SectionName));
        services.AddHostedService<AllocationRunHostedService>();
        services.Configure<InventoryOutboxOptions>(configuration.GetSection(InventoryOutboxOptions.SectionName));
        services.AddHostedService<InventoryOutboxHostedService>();
        services.AddSingleton<IPayOsGateway>(_ => new PayOsGateway(configuration));

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
