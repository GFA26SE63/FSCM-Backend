using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Notifications.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Notifications.Infrastructure;

public sealed class NotificationsModuleInstaller : IModuleInstaller
{
    public string Name => "Notifications";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "notifications")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<NotificationsDbContext>());
        s.AddScoped<INotificationUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());
        s.AddScoped<INotificationRepository, NotificationRepository>();
        s.AddScoped<IPushSender, LoggingPushSender>();
        s.AddNotificationsApplication();
    }
}
