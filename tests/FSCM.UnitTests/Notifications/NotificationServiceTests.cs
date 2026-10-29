using FSCM.Application.Features.Notifications;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Notifications;

public sealed class NotificationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task List_and_badges_are_scoped_to_the_authenticated_recipient()
    {
        var ownUnread = NewNotification(UserId, false);
        var ownRead = NewNotification(UserId, true);
        var otherUnread = NewNotification(Guid.NewGuid(), false);
        var store = new TestStore([ownUnread, ownRead, otherUnread]);
        var service = new NotificationService(store, TimeProvider.System);

        var page = await service.ListAsync(UserId, new(false, null), default);
        var badges = await service.GetBadgesAsync(UserId, default);

        Assert.Single(page.Items);
        Assert.Equal(ownUnread.Id, page.Items.Single().Id);
        Assert.Equal(1, badges.UnreadCount);
        Assert.True(badges.HasUnread);
    }

    [Fact]
    public async Task Mark_read_is_idempotent_and_cannot_access_another_recipient()
    {
        var own = NewNotification(UserId, false);
        var other = NewNotification(Guid.NewGuid(), false);
        var store = new TestStore([own, other]);
        var service = new NotificationService(store, TimeProvider.System);

        var first = await service.MarkReadAsync(UserId, own.Id, default);
        var second = await service.MarkReadAsync(UserId, own.Id, default);

        Assert.True(first.IsRead);
        Assert.Equal(first.ReadAt, second.ReadAt);
        Assert.Equal(1, store.SaveCount);
        var error = await Assert.ThrowsAsync<NotificationOperationException>(
            () => service.MarkReadAsync(UserId, other.Id, default));
        Assert.Equal("not_found", error.Code);
    }

    [Fact]
    public async Task Mark_all_updates_only_unread_notifications_owned_by_the_recipient()
    {
        var ownUnread = NewNotification(UserId, false);
        var ownRead = NewNotification(UserId, true);
        var otherUnread = NewNotification(Guid.NewGuid(), false);
        var store = new TestStore([ownUnread, ownRead, otherUnread]);
        var service = new NotificationService(store, TimeProvider.System);

        var result = await service.MarkAllReadAsync(UserId, default);

        Assert.Equal(1, result.UpdatedCount);
        Assert.True(ownUnread.IsRead);
        Assert.True(ownRead.IsRead);
        Assert.False(otherUnread.IsRead);
    }

    private static Notification NewNotification(Guid userId, bool isRead) => new()
    {
        Id = Guid.NewGuid(),
        UserAccountId = userId,
        Type = "OrderReviewed",
        Title = "Đơn hàng đã được duyệt",
        Message = "Đơn hàng đã sẵn sàng.",
        IsRead = isRead,
        CreatedAt = DateTimeOffset.UtcNow,
        ReadAt = isRead ? DateTimeOffset.UtcNow : null,
        RelatedEntityType = "SalesOrder",
        RelatedEntityId = Guid.NewGuid()
    };

    private sealed class TestStore(IEnumerable<Notification> notifications) : INotificationStore
    {
        private readonly List<Notification> _notifications = [.. notifications];
        public int SaveCount { get; private set; }

        public Task<(IReadOnlyCollection<Notification> Items, int TotalCount)> ListAsync(
            Guid userId, NotificationFilter filter, CancellationToken cancellationToken)
        {
            var query = _notifications.Where(item => item.UserAccountId == userId);
            if (filter.IsRead is { } isRead) query = query.Where(item => item.IsRead == isRead);
            if (filter.Type is { } type) query = query.Where(item => item.Type == type);
            var items = query.OrderByDescending(item => item.CreatedAt).ToArray();
            return Task.FromResult<(IReadOnlyCollection<Notification>, int)>((items, items.Length));
        }

        public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_notifications.Count(item => item.UserAccountId == userId && !item.IsRead));

        public Task<Notification?> FindOwnedAsync(
            Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(_notifications.SingleOrDefault(item =>
                item.UserAccountId == userId && item.Id == notificationId));

        public Task<int> MarkAllReadAsync(
            Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken)
        {
            var items = _notifications.Where(item => item.UserAccountId == userId && !item.IsRead).ToArray();
            foreach (var item in items) { item.IsRead = true; item.ReadAt = readAt; }
            return Task.FromResult(items.Length);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
