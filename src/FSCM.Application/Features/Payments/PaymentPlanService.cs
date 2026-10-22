using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public sealed class PaymentPlanService(IPaymentStore store) : IPaymentPlanService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task CreateForFinalApprovedOrderAsync(
        SalesOrder order,
        Guid approvalActorUserId,
        DateTimeOffset approvedAt,
        string finalApprovalReference,
        CancellationToken cancellationToken)
    {
        finalApprovalReference = string.IsNullOrWhiteSpace(finalApprovalReference)
            ? throw PaymentOperationException.Validation("Final approval reference is required.")
            : finalApprovalReference.Trim();
        if (finalApprovalReference.Length > 100)
            throw PaymentOperationException.Validation("Final approval reference cannot exceed 100 characters.");
        if (order.FinalAmount <= 0)
            throw PaymentOperationException.Conflict("Final approved amount must be positive.");

        var existing = await store.FindByOrderAsync(order.Id, true, cancellationToken);
        if (existing is not null)
        {
            if (existing.Payment.Method != order.PaymentMethod ||
                existing.Payment.Amount != order.FinalAmount ||
                existing.Payment.FinalApprovalReference != finalApprovalReference)
            {
                throw PaymentOperationException.Conflict(
                    "Existing payment ledger does not match the final approved order snapshot.");
            }
            return;
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            SalesOrderId = order.Id,
            Method = order.PaymentMethod,
            Status = PaymentStatuses.Unpaid,
            Amount = order.FinalAmount,
            ConfirmedAmount = 0,
            FinalApprovedAt = approvedAt,
            FinalApprovalReference = finalApprovalReference,
            Version = 0,
            CreatedAt = approvedAt,
            UpdatedAt = approvedAt
        };
        store.Add(payment);
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = approvalActorUserId,
            EntityName = nameof(Payment),
            EntityId = payment.Id,
            Action = "FinalPaymentLedgerCreated",
            NewValues = JsonSerializer.Serialize(new
            {
                payment.SalesOrderId,
                payment.Method,
                payment.Amount,
                payment.ConfirmedAmount,
                payment.FinalApprovedAt,
                payment.FinalApprovalReference,
                payment.Version
            }),
            Reason = "Khởi tạo sổ thanh toán từ giá trị phân bổ cuối cùng đã được duyệt",
            CreatedAt = approvedAt
        });
    }

    public async Task CreateForDeliveredOrderAsync(
        SalesOrder order,
        Guid receiptActorUserId,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken)
    {
        var existing = await store.FindByOrderAsync(order.Id, true, cancellationToken)
            ?? throw PaymentOperationException.Conflict(
                "Payment ledger must be created by final allocation approval before delivery.");
        if (existing.Payment.Method != order.PaymentMethod || existing.Payment.Amount != order.FinalAmount)
            throw PaymentOperationException.Conflict(
                "Existing payment does not match the delivered order snapshot.");

        var deliveredDate = DateOnly.FromDateTime(deliveredAt.ToOffset(VietnamOffset).DateTime);
        var isCredit = order.PaymentMethod == OrderPaymentMethods.Credit;
        if (isCredit && order.CreditTermDays is not (7 or 14 or 30))
            throw PaymentOperationException.Conflict("Credit term must be 7, 14, or 30 days.");

        if (existing.Payment.DeliveredDate.HasValue)
        {
            if (existing.Payment.DeliveredDate != deliveredDate)
                throw PaymentOperationException.Conflict("Payment delivery date does not match the receipt event.");
            return;
        }

        existing.Payment.DeliveredDate = deliveredDate;
        existing.Payment.DueDate = isCredit ? deliveredDate.AddDays(order.CreditTermDays) : deliveredDate;
        existing.Payment.Note = isCredit
            ? "Khoản phải thu bắt đầu tính hạn từ ngày nhận đủ hàng"
            : "Đã ghi nhận ngày giao đủ hàng; trạng thái thanh toán không tự động thay đổi";
        existing.Payment.Version++;
        existing.Payment.UpdatedAt = deliveredAt;
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = receiptActorUserId,
            EntityName = nameof(Payment),
            EntityId = existing.Payment.Id,
            Action = "DeliveryDateRecorded",
            NewValues = JsonSerializer.Serialize(new
            {
                existing.Payment.SalesOrderId,
                existing.Payment.Method,
                existing.Payment.Status,
                existing.Payment.Amount,
                existing.Payment.ConfirmedAmount,
                existing.Payment.DeliveredDate,
                existing.Payment.DueDate,
                existing.Payment.Version
            }),
            Reason = existing.Payment.Note,
            CreatedAt = deliveredAt
        });
    }
}
