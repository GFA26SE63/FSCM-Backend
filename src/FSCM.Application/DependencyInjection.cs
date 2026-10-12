using FluentValidation;
using FSCM.Application.Features.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddAutoMapper(_ => { }, typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IdentityService>();

        return services;
    }
}
