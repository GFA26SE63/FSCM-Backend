using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Identity.Contracts;
using FSCM.Modules.Notifications.Domain.Notifications;

namespace FSCM.Modules.Notifications.Application;

public interface INotificationUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface INotificationRepository
{
    void Add(Notification n);
    Task<Notification?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Notification>> ForUserAsync(string userId, bool unreadOnly, int take, CancellationToken ct);
    Task<int> UnreadCountAsync(string userId, CancellationToken ct);
}

/// <summary>Push channel (Azure Notification Hubs in production). The default implementation only logs, so the system runs without cloud keys.</summary>
public interface IPushSender
{
    Task SendAsync(IReadOnlyList<DeviceDto> devices, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken ct);
}
