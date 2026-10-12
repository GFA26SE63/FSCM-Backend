using FSCM.Application.Features.Identity;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(
                    connectionString,
                    postgresOptions => postgresOptions.EnableRetryOnFailure());
            });
        }

        services.AddScoped<IIdentityStore, IdentityStore>();

        return services;
    }
}
