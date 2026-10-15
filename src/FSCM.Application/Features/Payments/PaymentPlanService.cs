using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed class PaymentPlanService(IPaymentStore store) : IPaymentPlanService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task CreateForDeliveredOrderAsync(
        SalesOrder order,
        Guid receiptActorUserId,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindByOrderAsync(order.Id, true, cancellationToken);
        if (existing is not null)
        {
            if (existing.Payment.Method != order.PaymentMethod || existing.Payment.Amount != order.FinalAmount)
                throw PaymentOperationException.Conflict(
                    "Existing payment does not match the delivered order snapshot.");
            return;
        }

        var deliveredDate = DateOnly.FromDateTime(deliveredAt.ToOffset(VietnamOffset).DateTime);
        var isCredit = order.PaymentMethod == OrderPaymentMethods.Credit;
        if (isCredit && order.CreditTermDays is not (7 or 14 or 30))
            throw PaymentOperationException.Conflict("Credit term must be 7, 14, or 30 days.");

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            SalesOrderId = order.Id,
            Method = order.PaymentMethod,
            Status = isCredit ? PaymentStatuses.Unpaid : PaymentStatuses.Paid,
            Amount = order.FinalAmount,
            DeliveredDate = deliveredDate,
            DueDate = isCredit ? deliveredDate.AddDays(order.CreditTermDays) : deliveredDate,
            PaidDate = isCredit ? null : deliveredDate,
            CollectionMethod = isCredit ? null : PaymentCollectionMethods.DeliverySettlement,
            ConfirmedByUserId = isCredit ? null : receiptActorUserId,
            ConfirmedAt = isCredit ? null : deliveredAt,
            Note = isCredit ? "Khoản phải thu được tạo khi nhà bán lẻ xác nhận nhận hàng" :
                "Tự động tất toán khi nhà bán lẻ xác nhận nhận hàng",
            Version = 0,
            CreatedAt = deliveredAt,
            UpdatedAt = deliveredAt
        };
        store.Add(payment);
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = receiptActorUserId,
            EntityName = nameof(Payment),
            EntityId = payment.Id,
            Action = isCredit ? "CreatedReceivable" : "SettledAtDelivery",
            NewValues = JsonSerializer.Serialize(new
            {
                payment.SalesOrderId,
                payment.Method,
                payment.Status,
                payment.Amount,
                payment.DeliveredDate,
                payment.DueDate,
                payment.PaidDate,
                payment.CollectionMethod,
                payment.Version
            }),
            Reason = payment.Note,
            CreatedAt = deliveredAt
        });
    }
}
