using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Notifications;

public sealed class NotificationService(INotificationStore store, TimeProvider timeProvider)
{
    public async Task<NotificationPage> ListAsync(
        Guid userId, NotificationFilter filter, CancellationToken cancellationToken)
    {
        ValidateUser(userId);
        if (filter.Page < 1)
            throw NotificationOperationException.Validation("Page must be at least 1.");
        if (filter.PageSize is < 1 or > 100)
            throw NotificationOperationException.Validation("Page size must be between 1 and 100.");
        if (filter.Type?.Trim().Length > 50)
            throw NotificationOperationException.Validation("Notification type cannot exceed 50 characters.");

        var normalized = filter with { Type = NullIfWhiteSpace(filter.Type) };
        var (items, totalCount) = await store.ListAsync(userId, normalized, cancellationToken);
        return new(items.Select(Map).ToArray(), normalized.Page, normalized.PageSize, totalCount);
    }

    public async Task<NotificationBadgeResult> GetBadgesAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        ValidateUser(userId);
        var unreadCount = await store.CountUnreadAsync(userId, cancellationToken);
        return new(unreadCount, unreadCount > 0);
    }

    public async Task<NotificationResult> MarkReadAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        ValidateUser(userId);
        if (notificationId == Guid.Empty)
            throw NotificationOperationException.Validation("Notification identifier is required.");

        var notification = await store.FindOwnedAsync(userId, notificationId, cancellationToken)
            ?? throw NotificationOperationException.NotFound();
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
        }

        return Map(notification);
    }

    public async Task<MarkAllNotificationsReadResult> MarkAllReadAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        ValidateUser(userId);
        var readAt = timeProvider.GetUtcNow();
        var updatedCount = await store.MarkAllReadAsync(userId, readAt, cancellationToken);
        return new(updatedCount, readAt);
    }

    private static NotificationResult Map(Notification notification)
    {
        var relatedEntityType = notification.RelatedEntityType;
        var relatedEntityId = notification.RelatedEntityId;
        var deepLink = notification.DeepLink;
        if (relatedEntityId is null && notification.SalesOrderId is { } orderId)
        {
            relatedEntityType = "SalesOrder";
            relatedEntityId = orderId;
            deepLink ??= $"/orders/{orderId}";
        }

        return new(notification.Id, notification.Type, notification.Title, notification.Message,
            notification.IsRead, notification.CreatedAt, notification.ReadAt,
            relatedEntityType, relatedEntityId, deepLink);
    }

    private static void ValidateUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw NotificationOperationException.Validation("Authenticated user identifier is invalid.");
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
