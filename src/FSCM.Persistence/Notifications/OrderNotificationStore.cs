using FSCM.Application.Features.Notifications;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Notifications;

public sealed class OrderNotificationStore(AppDbContext db) : IOrderNotificationStore
{
    public Task<Guid?> FindRetailerUserIdAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        (from order in db.SalesOrderRecords.AsNoTracking()
         join retailer in db.RetailerRecords.AsNoTracking() on order.RetailerId equals retailer.Id
         where order.Id == salesOrderId
         select retailer.UserAccountId).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid userAccountId, string type, Guid salesOrderId,
        Guid? relatedEntityId, CancellationToken cancellationToken) =>
        db.NotificationRecords.AsNoTracking().AnyAsync(item =>
            item.UserAccountId == userAccountId &&
            item.Type == type &&
            item.SalesOrderId == salesOrderId &&
            item.RelatedEntityId == (relatedEntityId ?? salesOrderId), cancellationToken);

    public void Add(Notification notification) => db.NotificationRecords.Add(notification);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
