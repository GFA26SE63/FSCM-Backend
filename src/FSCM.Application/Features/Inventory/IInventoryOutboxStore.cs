using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public interface IInventoryOutboxStore
{
    Task<IReadOnlyCollection<OutboxMessage>> ListPendingBatchExpiredMessagesAsync(
        int take, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> ListExpiryNotificationRecipientsAsync(
        Guid warehouseId, CancellationToken cancellationToken);
    Task<bool> NotificationExistsAsync(
        Guid userAccountId, Guid outboxMessageId, CancellationToken cancellationToken);
    void AddNotification(Notification notification);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
