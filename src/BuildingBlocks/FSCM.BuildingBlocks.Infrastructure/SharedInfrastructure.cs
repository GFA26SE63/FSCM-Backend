using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Events;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.BuildingBlocks.Infrastructure;

public static class SharedInfrastructure
{
    /// <summary>Clock, id sequence (shared schema), event bus, IdGenerator. Called once by each host before the module installers.</summary>
    public static IServiceCollection AddFscmSharedInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<SharedDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "shared")));
        s.AddSingleton<IClock, SystemClock>();
        s.AddScoped<ISequenceGenerator, PostgresSequenceGenerator>();
        s.AddScoped<IdGenerator>();
        s.AddScoped<IEventBus, InProcessEventBus>();
        return s;
    }
}
