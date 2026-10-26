using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Notifications;

public sealed record NotificationFilter(bool? IsRead, string? Type, int Page = 1, int PageSize = 20);

public sealed record NotificationResult(
    Guid Id,
    string Type,
    string Title,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string? DeepLink);

public sealed record NotificationPage(
    IReadOnlyCollection<NotificationResult> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record NotificationBadgeResult(int UnreadCount, bool HasUnread);

public sealed record MarkAllNotificationsReadResult(int UpdatedCount, DateTimeOffset ReadAt);

public interface INotificationStore
{
    Task<(IReadOnlyCollection<Notification> Items, int TotalCount)> ListAsync(
        Guid userId, NotificationFilter filter, CancellationToken cancellationToken);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);
    Task<Notification?> FindOwnedAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);
    Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class NotificationOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static NotificationOperationException Validation(string message) => new("validation_error", message);
    public static NotificationOperationException NotFound() => new("not_found", "Notification was not found.");
}
