using FSCM.Application.Features.Notifications;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Notifications;

public sealed class NotificationStore(AppDbContext dbContext) : INotificationStore
{
    public async Task<(IReadOnlyCollection<Notification> Items, int TotalCount)> ListAsync(
        Guid userId, NotificationFilter filter, CancellationToken cancellationToken)
    {
        var query = dbContext.NotificationRecords.AsNoTracking()
            .Where(item => item.UserAccountId == userId);
        if (filter.IsRead is { } isRead) query = query.Where(item => item.IsRead == isRead);
        if (filter.Type is { } type) query = query.Where(item => item.Type == type);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.NotificationRecords.AsNoTracking()
            .CountAsync(item => item.UserAccountId == userId && !item.IsRead, cancellationToken);

    public Task<Notification?> FindOwnedAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
        dbContext.NotificationRecords.SingleOrDefaultAsync(
            item => item.Id == notificationId && item.UserAccountId == userId, cancellationToken);

    public Task<int> MarkAllReadAsync(
        Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken) =>
        dbContext.NotificationRecords
            .Where(item => item.UserAccountId == userId && !item.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsRead, true)
                .SetProperty(item => item.ReadAt, readAt), cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
