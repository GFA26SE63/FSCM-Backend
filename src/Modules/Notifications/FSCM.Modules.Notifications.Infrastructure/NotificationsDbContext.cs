using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Identity.Contracts;
using FSCM.Modules.Notifications.Application;
using FSCM.Modules.Notifications.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;

namespace FSCM.Modules.Notifications.Infrastructure;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> o) : ModuleDbContext(o), INotificationUnitOfWork
{
    protected override string Schema => "notifications";
    public DbSet<Notification> Notifications => Set<Notification>();
}

internal sealed class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notification"); b.HasKey(x => x.Id); b.Property(x => x.UserId).HasMaxLength(40); b.Property(x => x.Type).HasMaxLength(40);
        b.Property(x => x.Title).HasMaxLength(200); b.Property(x => x.Body).HasMaxLength(600); b.Property(x => x.Reference).HasMaxLength(40);
        b.Ignore(x => x.IsRead); b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
    }
}
internal sealed class NotificationRepository(NotificationsDbContext db) : INotificationRepository
{
    public void Add(Notification n) => db.Notifications.Add(n);
    public Task<Notification?> GetAsync(Guid id, CancellationToken ct) => db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
    public async Task<IReadOnlyList<Notification>> ForUserAsync(string userId, bool unreadOnly, int take, CancellationToken ct) =>
        await db.Notifications.Where(n => n.UserId == userId && (!unreadOnly || n.ReadAtUtc == null)).OrderByDescending(n => n.CreatedAtUtc).Take(take).ToListAsync(ct);
    public Task<int> UnreadCountAsync(string userId, CancellationToken ct) => db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAtUtc == null, ct);
}

/// <summary>
/// Default push channel: writes to the log. Replace with an Azure Notification Hubs implementation
/// (Microsoft.Azure.NotificationHubs: NotificationHubClient.SendDirectNotificationAsync per device handle) and register it instead.
/// </summary>
internal sealed class LoggingPushSender(ILogger<LoggingPushSender> log) : IPushSender
{
    public Task SendAsync(IReadOnlyList<DeviceDto> devices, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        log.LogInformation("PUSH to {Count} device(s): {Title} – {Body}", devices.Count, title, body);
        return Task.CompletedTask;
    }
}
