using System.Reflection;

namespace FSCM.API.Extensions;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddFmcgConfiguration(
        this IConfigurationBuilder configuration,
        IWebHostEnvironment environment)
    {
        configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment.EnvironmentName}.Local.json", optional: true, reloadOnChange: true);

        if (environment.IsDevelopment())
        {
            configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);
        }

        configuration.AddEnvironmentVariables();
        configuration.AddEnvironmentVariables(prefix: "FSCM_");

        return configuration;
    }
}
