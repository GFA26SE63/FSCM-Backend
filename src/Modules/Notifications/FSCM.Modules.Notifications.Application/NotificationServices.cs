using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Contracts;
using FSCM.Modules.Identity.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Loyalty.Contracts;
using FSCM.Modules.Notifications.Contracts;
using FSCM.Modules.Notifications.Domain.Notifications;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Partners.Contracts;
using FSCM.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Notifications.Application;

public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, string? Reference, DateTime CreatedAtUtc, bool IsRead);
public sealed record NotificationListDto(int Unread, IReadOnlyList<NotificationDto> Items);

/// <summary>Creates in-app notifications and sends the push copy to every registered device of the recipients.</summary>
public sealed class NotificationDispatcher(INotificationRepository repo, INotificationUnitOfWork uow, IIdentityModule identity, IPushSender push, IClock clock) : INotificationModule
{
    public Task NotifyAsync(string userId, string type, string title, string body, string? reference = null, CancellationToken ct = default) =>
        SendAsync(new[] { userId }, type, (title, body), reference, ct);

    public async Task SendAsync(IEnumerable<string?> userIds, string type, (string Title, string Body) message, string? reference, CancellationToken ct)
    {
        var recipients = userIds.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct().ToList();
        if (recipients.Count == 0) return;
        foreach (var id in recipients) repo.Add(new Notification(id, type, message.Title, message.Body, reference, clock.UtcNow));
        await uow.SaveChangesAsync(ct);
        var data = new Dictionary<string, string> { ["type"] = type, ["ref"] = reference ?? "" };
        foreach (var id in recipients)
        {
            var devices = await identity.DevicesOfAsync(id, ct);
            if (devices.Count > 0) await push.SendAsync(devices, message.Title, message.Body, data, ct);
        }
    }

    public async Task<IEnumerable<string>> RolesAsync(string role, string? warehouse = null, CancellationToken ct = default) =>
        (await identity.UsersInRoleAsync(role, warehouse, ct)).Select(u => u.Id);
    public async Task<IEnumerable<string>> StaffAsync(CancellationToken ct = default) =>
        (await RolesAsync("Operator", null, ct)).Concat(await RolesAsync("Admin", null, ct));
    public async Task<IEnumerable<string>> RetailerUsersAsync(string retailerCode, CancellationToken ct = default) =>
        (await identity.UsersOfRetailerAsync(retailerCode, ct)).Select(u => u.Id);
}

/// <summary>Bell icon: list and mark as read, always for the current user.</summary>
public sealed class NotificationService(INotificationRepository repo, INotificationUnitOfWork uow, ICurrentUser user, IClock clock)
{
    public async Task<NotificationListDto> ListAsync(bool unreadOnly, int take, CancellationToken ct) =>
        new(await repo.UnreadCountAsync(user.UserId, ct),
            (await repo.ForUserAsync(user.UserId, unreadOnly, Math.Clamp(take, 1, 200), ct)).Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Reference, n.CreatedAtUtc, n.IsRead)).ToList());

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var n = await repo.GetAsync(id, ct);
        Guard.Against(n is null || n.UserId != user.UserId, "notification.not_found", "Không có thông báo.");
        n!.MarkRead(clock.UtcNow); await uow.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Notifications ← every user-facing event (SPEC §4). One handler class, one method per event; recipients are resolved through Identity
/// (roles, warehouse keepers, retailer accounts) so no module needs to know about notifications.
/// </summary>
public sealed class EventNotificationHandlers(NotificationDispatcher n) :
    IIntegrationEventHandler<OrderSubmitted>, IIntegrationEventHandler<OrderApproved>, IIntegrationEventHandler<OrderRejected>, IIntegrationEventHandler<OrderCancelled>,
    IIntegrationEventHandler<OrderShipped>, IIntegrationEventHandler<OrderDelivered>, IIntegrationEventHandler<ReceivableOverdue>, IIntegrationEventHandler<CreditOrderPaid>,
    IIntegrationEventHandler<BatchExpired>, IIntegrationEventHandler<RotationProposalsCreated>, IIntegrationEventHandler<TransferCreated>,
    IIntegrationEventHandler<GiftRedemptionRequested>, IIntegrationEventHandler<GiftRedemptionApproved>, IIntegrationEventHandler<GiftRedemptionRejected>, IIntegrationEventHandler<GiftRedemptionDelivered>,
    IIntegrationEventHandler<RetailerRegistered>, IIntegrationEventHandler<RetailerRegistrationRejected>, IIntegrationEventHandler<ComplaintCreated>
{
    public async Task HandleAsync(OrderSubmitted e, CancellationToken ct = default) =>
        await n.SendAsync(await n.StaffAsync(ct), nameof(OrderSubmitted), Templates.OrderSubmitted(e.OrderCode, e.RetailerName, e.NetAmount), e.OrderCode, ct);
    public async Task HandleAsync(OrderApproved e, CancellationToken ct = default) =>
        await n.SendAsync((await n.RetailerUsersAsync(e.RetailerCode, ct)).Append(e.SalesUserId), nameof(OrderApproved), Templates.OrderApproved(e.OrderCode), e.OrderCode, ct);
    public async Task HandleAsync(OrderRejected e, CancellationToken ct = default) =>
        await n.SendAsync((await n.RetailerUsersAsync(e.RetailerCode, ct)).Append(e.SalesUserId), nameof(OrderRejected), Templates.OrderRejected(e.OrderCode, e.Reason), e.OrderCode, ct);
    public async Task HandleAsync(OrderCancelled e, CancellationToken ct = default) =>
        await n.SendAsync((await n.StaffAsync(ct)).Append(e.SalesUserId), nameof(OrderCancelled), Templates.OrderCancelled(e.OrderCode), e.OrderCode, ct);
    public async Task HandleAsync(OrderShipped e, CancellationToken ct = default) =>
        await n.SendAsync((await n.RetailerUsersAsync(e.RetailerCode, ct)).Append(e.SalesUserId), nameof(OrderShipped), Templates.OrderShipped(e.OrderCode, e.Plate, e.DriverName, e.DriverPhone), e.OrderCode, ct);
    public Task HandleAsync(OrderDelivered e, CancellationToken ct = default) =>
        n.SendAsync(new[] { e.SalesUserId }, nameof(OrderDelivered), Templates.OrderDelivered(e.OrderCode), e.OrderCode, ct);
    public async Task HandleAsync(ReceivableOverdue e, CancellationToken ct = default) =>
        await n.SendAsync((await n.StaffAsync(ct)).Concat(await n.RetailerUsersAsync(e.RetailerCode, ct)).Append(e.SalesUserId), nameof(ReceivableOverdue), Templates.ReceivableOverdue(e.OrderCode, e.Amount, e.DaysOverdue), e.OrderCode, ct);
    public async Task HandleAsync(CreditOrderPaid e, CancellationToken ct = default) =>
        await n.SendAsync((await n.RetailerUsersAsync(e.RetailerCode, ct)).Append(e.SalesUserId), nameof(CreditOrderPaid), Templates.CreditPaid(e.OrderCode, e.Amount), e.OrderCode, ct);
    public async Task HandleAsync(BatchExpired e, CancellationToken ct = default) =>
        await n.SendAsync((await n.RolesAsync("Admin", null, ct)).Concat(await n.RolesAsync("WarehouseKeeper", e.Warehouse, ct)), nameof(BatchExpired), Templates.BatchExpired(e.BatchCode, e.Quantity), e.BatchCode, ct);
    public async Task HandleAsync(RotationProposalsCreated e, CancellationToken ct = default)
    { if (e.Count > 0) await n.SendAsync(await n.RolesAsync("Admin", null, ct), nameof(RotationProposalsCreated), Templates.RotationProposals(e.Count), e.RunDate.ToString("yyyy-MM-dd"), ct); }
    public async Task HandleAsync(TransferCreated e, CancellationToken ct = default) =>
        await n.SendAsync(await n.RolesAsync("WarehouseKeeper", e.To, ct), nameof(TransferCreated), Templates.TransferCreated(e.TransferCode, e.From, e.Sku, e.Quantity), e.TransferCode, ct);
    public async Task HandleAsync(GiftRedemptionRequested e, CancellationToken ct = default) =>
        await n.SendAsync(await n.StaffAsync(ct), nameof(GiftRedemptionRequested), Templates.GiftRequested(e.RedemptionCode, e.RetailerCode, e.GiftName), e.RedemptionCode, ct);
    public async Task HandleAsync(GiftRedemptionApproved e, CancellationToken ct = default) =>
        await n.SendAsync(await n.RetailerUsersAsync(e.RetailerCode, ct), nameof(GiftRedemptionApproved), Templates.GiftApproved(e.RedemptionCode, e.GiftName), e.RedemptionCode, ct);
    public async Task HandleAsync(GiftRedemptionRejected e, CancellationToken ct = default) =>
        await n.SendAsync(await n.RetailerUsersAsync(e.RetailerCode, ct), nameof(GiftRedemptionRejected), Templates.GiftRejected(e.RedemptionCode, e.GiftName, e.PointsRefunded, e.Reason), e.RedemptionCode, ct);
    public async Task HandleAsync(GiftRedemptionDelivered e, CancellationToken ct = default) =>
        await n.SendAsync(await n.RetailerUsersAsync(e.RetailerCode, ct), nameof(GiftRedemptionDelivered), Templates.GiftDelivered(e.RedemptionCode, e.GiftName), e.RedemptionCode, ct);
    public Task HandleAsync(RetailerRegistered e, CancellationToken ct = default) =>
        n.SendAsync(new[] { e.DeclaredByUserId }, nameof(RetailerRegistered), Templates.RetailerRegistered(e.Name, e.RetailerCode), e.RequestCode, ct);
    public Task HandleAsync(RetailerRegistrationRejected e, CancellationToken ct = default) =>
        n.SendAsync(new[] { e.DeclaredByUserId }, nameof(RetailerRegistrationRejected), Templates.RetailerRejected(e.Name, e.Reason), e.RequestCode, ct);
    public async Task HandleAsync(ComplaintCreated e, CancellationToken ct = default) =>
        await n.SendAsync(await n.StaffAsync(ct), nameof(ComplaintCreated), Templates.ComplaintCreated(e.ComplaintCode, e.OrderCode), e.ComplaintCode, ct);
}

public static class NotificationsApplication
{
    public static IServiceCollection AddNotificationsApplication(this IServiceCollection s)
    {
        s.AddScoped<NotificationDispatcher>().AddScoped<INotificationModule>(sp => sp.GetRequiredService<NotificationDispatcher>());
        s.AddScoped<NotificationService>().AddScoped<EventNotificationHandlers>();
        return s
            .AddHandler<OrderSubmitted>().AddHandler<OrderApproved>().AddHandler<OrderRejected>().AddHandler<OrderCancelled>().AddHandler<OrderShipped>().AddHandler<OrderDelivered>()
            .AddHandler<ReceivableOverdue>().AddHandler<CreditOrderPaid>().AddHandler<BatchExpired>().AddHandler<RotationProposalsCreated>().AddHandler<TransferCreated>()
            .AddHandler<GiftRedemptionRequested>().AddHandler<GiftRedemptionApproved>().AddHandler<GiftRedemptionRejected>().AddHandler<GiftRedemptionDelivered>()
            .AddHandler<RetailerRegistered>().AddHandler<RetailerRegistrationRejected>().AddHandler<ComplaintCreated>();
    }
    private static IServiceCollection AddHandler<T>(this IServiceCollection s) where T : IIntegrationEvent =>
        s.AddScoped<IIntegrationEventHandler<T>>(sp => (IIntegrationEventHandler<T>)(object)sp.GetRequiredService<EventNotificationHandlers>());
}
