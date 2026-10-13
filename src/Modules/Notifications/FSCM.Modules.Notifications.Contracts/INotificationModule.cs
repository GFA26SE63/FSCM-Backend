namespace FSCM.Modules.Notifications.Contracts;

/// <summary>Direct call for modules that need to notify a specific user without publishing an event.</summary>
public interface INotificationModule
{
    Task NotifyAsync(string userId, string type, string title, string body, string? reference = null, CancellationToken ct = default);
}
