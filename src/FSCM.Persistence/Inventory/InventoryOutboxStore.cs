using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Inventory;

public sealed class InventoryOutboxStore(AppDbContext dbContext) : IInventoryOutboxStore
{
    public async Task<IReadOnlyCollection<OutboxMessage>> ListPendingBatchExpiredMessagesAsync(
        int take, CancellationToken cancellationToken) =>
        await dbContext.OutboxMessageRecords
            .Where(item => item.EventType == InventoryOutboxEventTypes.BatchExpired &&
                           item.Status == OutboxMessageStatuses.Pending)
            .OrderBy(item => item.CreatedAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> ListExpiryNotificationRecipientsAsync(
        Guid warehouseId, CancellationToken cancellationToken)
    {
        var administrators = dbContext.UserAccountRecords.AsNoTracking()
            .Where(item => item.IsActive && item.Role == SystemRoles.Admin)
            .Select(item => item.Id);
        var keepers =
            from assignment in dbContext.UserWarehouseAssignmentRecords.AsNoTracking()
            join user in dbContext.UserAccountRecords.AsNoTracking()
                on assignment.UserAccountId equals user.Id
            where assignment.WarehouseId == warehouseId && assignment.IsActive &&
                  user.IsActive && user.Role == SystemRoles.WarehouseKeeper
            select user.Id;
        return await administrators.Concat(keepers).Distinct().ToArrayAsync(cancellationToken);
    }

    public Task<bool> NotificationExistsAsync(
        Guid userAccountId, Guid outboxMessageId, CancellationToken cancellationToken) =>
        dbContext.NotificationRecords.AsNoTracking().AnyAsync(item =>
            item.UserAccountId == userAccountId && item.OutboxMessageId == outboxMessageId,
            cancellationToken);

    public void AddNotification(Notification notification) =>
        dbContext.NotificationRecords.Add(notification);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
