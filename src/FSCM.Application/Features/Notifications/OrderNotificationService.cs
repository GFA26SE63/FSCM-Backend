using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Notifications;

public interface IOrderNotificationStore
{
    Task<Guid?> FindRetailerUserIdAsync(Guid salesOrderId, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid userAccountId, string type, Guid salesOrderId,
        Guid? relatedEntityId, CancellationToken cancellationToken);
    void Add(Notification notification);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Writes idempotent in-app notifications for the retailer that owns an order. Event producers
/// use this service after their own transaction has committed, so notification delivery can be
/// retried without repeating the commercial action.
/// </summary>
public sealed class OrderNotificationService(IOrderNotificationStore store, TimeProvider timeProvider)
{
    public async Task PublishRetailerAsync(
        Guid salesOrderId,
        string type,
        string title,
        string message,
        Guid? relatedEntityId,
        string? deepLink,
        CancellationToken cancellationToken)
    {
        var userId = await store.FindRetailerUserIdAsync(salesOrderId, cancellationToken);
        if (!userId.HasValue) return;
        if (await store.ExistsAsync(userId.Value, type, salesOrderId, relatedEntityId, cancellationToken))
            return;

        store.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId.Value,
            SalesOrderId = salesOrderId,
            RelatedEntityType = relatedEntityId.HasValue ? "SalesOrderEvent" : "SalesOrder",
            RelatedEntityId = relatedEntityId ?? salesOrderId,
            DeepLink = deepLink ?? $"/retailer/orders/{salesOrderId}",
            Type = Normalize(type, 50),
            Title = Normalize(title, 200),
            Message = string.IsNullOrWhiteSpace(message) ? title.Trim() : message.Trim(),
            CreatedAt = timeProvider.GetUtcNow()
        });
        await store.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string value, int maxLength)
    {
        value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Notification value is required.", nameof(value))
            : value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
