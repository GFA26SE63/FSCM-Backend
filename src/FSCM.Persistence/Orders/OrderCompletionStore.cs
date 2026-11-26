using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Orders;

public sealed class OrderCompletionStore(AppDbContext dbContext) : IOrderCompletionStore
{
    public async Task<OrderCompletionView?> FindAsync(
        Guid salesOrderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrderRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == salesOrderId, cancellationToken);
        if (order is null) return null;

        // Paid means the ledger reached Paid, whatever route the money took: a prepaid transfer
        // verified before dispatch and a credit term collected weeks later both land here.
        var payment = await dbContext.PaymentRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SalesOrderId == salesOrderId, cancellationToken);

        // Open means not yet resolved, read from the timestamp rather than the status string, so a
        // new status spelling cannot quietly make disputes invisible.
        var openComplaint = await dbContext.ReceiptDiscrepancyRecords.AsNoTracking()
            .AnyAsync(item => item.SalesOrderId == salesOrderId && item.ResolvedAt == null,
                cancellationToken);

        DateTimeOffset? deliveredAt = null;
        if (order.Status == OrderStatuses.Delivered)
        {
            deliveredAt = await dbContext.OrderStatusHistoryRecords.AsNoTracking()
                .Where(item => item.SalesOrderId == salesOrderId && item.ToStatus == OrderStatuses.Delivered)
                .OrderByDescending(item => item.ChangedAt)
                .Select(item => (DateTimeOffset?)item.ChangedAt)
                .FirstOrDefaultAsync(cancellationToken) ?? order.UpdatedAt;
        }

        var graceDays = 0;
        var rawGrace = await dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.ConfigKey == BusinessConfigurationKeys.OrderCompletionGraceDays && item.IsActive)
            .OrderByDescending(item => item.Version)
            .Select(item => item.ConfigValue)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(rawGrace) && int.TryParse(rawGrace, out var parsed) && parsed > 0)
        {
            graceDays = parsed;
        }

        return new OrderCompletionView(
            salesOrderId,
            order.Status,
            payment is not null && payment.Status == PaymentStatuses.Paid,
            openComplaint,
            deliveredAt,
            graceDays);
    }

    public async Task<IReadOnlyCollection<Guid>> ListDeliveredOrderIdsAsync(CancellationToken cancellationToken) =>
        await dbContext.SalesOrderRecords.AsNoTracking()
            .Where(item => item.Status == OrderStatuses.Delivered)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);

    public async Task CompleteAsync(
        Guid salesOrderId, Guid? actorUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrderRecords
            .SingleOrDefaultAsync(item => item.Id == salesOrderId, cancellationToken);
        if (order is null || order.Status == OrderStatuses.Completed) return;

        var from = order.Status;
        order.Status = OrderStatuses.Completed;
        order.Version++;
        order.UpdatedAt = now;

        dbContext.OrderStatusHistoryRecords.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            SalesOrderId = salesOrderId,
            ChangedByUserId = actorUserId ?? Guid.Empty,
            FromStatus = from,
            ToStatus = OrderStatuses.Completed,
            Reason = "Đã nhận đủ, đã thanh toán đủ và không còn khiếu nại đang xử lý.",
            ChangedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
