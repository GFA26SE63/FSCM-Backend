using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Notifications;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class NotificationApiTests
{
    [Fact]
    public async Task Rest_and_graphql_share_recipient_scoped_list_and_badge_contracts()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var user = identityFactory.AddTestUser("notifications@test.local", SystemRoles.Sales);
        var store = new TestStore(user.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationStore>();
            services.AddSingleton<INotificationStore>(store);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", identityFactory.CreateToken(user));

        var rest = await client.GetAsync("/api/v1/notifications?isRead=false&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, restBody.GetProperty("totalCount").GetInt32());
        Assert.Equal(store.OwnNotificationId,
            restBody.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal("/orders/" + store.OrderId,
            restBody.GetProperty("items")[0].GetProperty("deepLink").GetString());

        var badge = await client.GetFromJsonAsync<NotificationBadgeResult>("/api/v1/notifications/badges");
        Assert.Equal(1, badge!.UnreadCount);

        var graphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ notifications(filter: { isRead: false, page: 1, pageSize: 20 }) " +
                    "{ totalCount items { id deepLink } } notificationBadges { unreadCount hasUnread } }"
        });
        Assert.Equal(HttpStatusCode.OK, graphQl.StatusCode);
        var graphBody = JsonDocument.Parse(await graphQl.Content.ReadAsStringAsync()).RootElement;
        Assert.False(graphBody.TryGetProperty("errors", out _), graphBody.ToString());
        Assert.Equal(1, graphBody.GetProperty("data").GetProperty("notifications")
            .GetProperty("totalCount").GetInt32());
        Assert.Equal(1, graphBody.GetProperty("data").GetProperty("notificationBadges")
            .GetProperty("unreadCount").GetInt32());

        var forbidden = await client.PatchAsync(
            $"/api/v1/notifications/{store.OtherNotificationId}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var marked = await client.PatchAsync(
            $"/api/v1/notifications/{store.OwnNotificationId}/read", null);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<NotificationBadgeResult>(
            "/api/v1/notifications/badges"))!.UnreadCount);
    }

    private sealed class TestStore : INotificationStore
    {
        private readonly List<Notification> _items;
        public Guid OwnNotificationId { get; } = Guid.NewGuid();
        public Guid OtherNotificationId { get; } = Guid.NewGuid();
        public Guid OrderId { get; } = Guid.NewGuid();

        public TestStore(Guid userId)
        {
            _items =
            [
                New(OwnNotificationId, userId, OrderId),
                New(OtherNotificationId, Guid.NewGuid(), Guid.NewGuid())
            ];
        }

        public Task<(IReadOnlyCollection<Notification> Items, int TotalCount)> ListAsync(
            Guid userId, NotificationFilter filter, CancellationToken cancellationToken)
        {
            var query = _items.Where(item => item.UserAccountId == userId);
            if (filter.IsRead is { } isRead) query = query.Where(item => item.IsRead == isRead);
            var items = query.ToArray();
            return Task.FromResult<(IReadOnlyCollection<Notification>, int)>((items, items.Length));
        }

        public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.Count(item => item.UserAccountId == userId && !item.IsRead));

        public Task<Notification?> FindOwnedAsync(
            Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.SingleOrDefault(item =>
                item.UserAccountId == userId && item.Id == notificationId));

        public Task<int> MarkAllReadAsync(
            Guid userId, DateTimeOffset readAt, CancellationToken cancellationToken)
        {
            var items = _items.Where(item => item.UserAccountId == userId && !item.IsRead).ToArray();
            foreach (var item in items) { item.IsRead = true; item.ReadAt = readAt; }
            return Task.FromResult(items.Length);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static Notification New(Guid id, Guid userId, Guid orderId) => new()
        {
            Id = id,
            UserAccountId = userId,
            SalesOrderId = orderId,
            Type = "OrderReviewed",
            Title = "Đơn hàng đã được duyệt",
            Message = "Đơn hàng đã sẵn sàng.",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
