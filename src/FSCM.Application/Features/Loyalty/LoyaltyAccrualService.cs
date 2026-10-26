using FSCM.Application.Features.Orders;

namespace FSCM.Application.Features.Loyalty;

/// <summary>
/// F70: an order earns its points once, when it has actually been delivered and paid for and
/// nothing about it is in dispute.
///
/// This is written as one evaluation rather than three triggers on purpose. Three different events
/// can be the last condition to fall into place - the final warehouse part being received, the
/// money being collected, a complaint being settled - and which one comes last depends on the
/// payment method and on the retailer. Each of those callers asks the same question and this
/// answers it; none of them needs to know what the others have done.
///
/// It follows that evaluating is cheap and safe to repeat. An order that has already earned is
/// returned unchanged, so a caller may ask after every receipt and every payment without guarding.
///
/// The value is the order's approved final amount - what the run settled on and froze - not the
/// list price and not what today's discount table would say. A retailer who was charged the
/// approved price should earn on the approved price, and a discount table published next week must
/// not quietly change what an order that shipped last week was worth.
/// </summary>
public sealed class LoyaltyAccrualService(ILoyaltyAccrualStore store, TimeProvider timeProvider)
{
    /// <summary>
    /// Delivered is the end of the line for an order: there is no later "completed" state, so this
    /// is the one that means every warehouse part reached the retailer.
    /// </summary>
    private static readonly string[] ReceivedStatuses = [OrderStatuses.Delivered];

    public async Task<LoyaltyAccrualResult> EvaluateAsync(
        Guid salesOrderId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var view = await store.FindAsync(salesOrderId, cancellationToken);
        if (view is null)
        {
            return new(salesOrderId, false, 0, 0, LoyaltyAccrualBlock.None,
                "Không tìm thấy đơn; không có gì để cộng điểm.");
        }

        // Already earned. Saying so plainly is the whole of "cộng điểm một lần": a second receipt
        // confirmation, a late payment record or a replayed message must not pay twice.
        if (view.AlreadyEarned)
        {
            return new(salesOrderId, false, 0, view.Balance?.CurrentPoints ?? 0,
                LoyaltyAccrualBlock.None, "Đơn này đã được cộng điểm rồi.");
        }

        if (view.Program is null)
        {
            return await HoldAsync(view, 0, LoyaltyAccrualBlock.NoActiveProgram,
                "Chưa có chương trình tích điểm nào đang chạy.", actorUserId, cancellationToken);
        }

        var points = PointsFor(view.ApprovedFinalAmount, view.Program.SpendAmountPerPoint);

        if (!ReceivedStatuses.Contains(view.OrderStatus))
        {
            return await HoldAsync(view, points, LoyaltyAccrualBlock.NotFullyReceived,
                "Đơn chưa nhận đủ mọi phần giao.", actorUserId, cancellationToken);
        }

        if (!view.PaymentSettled)
        {
            return await HoldAsync(view, points, LoyaltyAccrualBlock.NotFullyPaid,
                "Đơn chưa thanh toán đủ giá trị thực.", actorUserId, cancellationToken);
        }

        if (view.HasOpenComplaint)
        {
            return await HoldAsync(view, points, LoyaltyAccrualBlock.ComplaintOpen,
                "Đơn còn khiếu nại đang xử lý.", actorUserId, cancellationToken);
        }

        if (points <= 0)
        {
            return await HoldAsync(view, 0, LoyaltyAccrualBlock.NothingToEarn,
                "Giá trị đơn chưa đủ một điểm.", actorUserId, cancellationToken);
        }

        var balance = view.Balance
            ?? await store.EnsureBalanceAsync(view.RetailerId, timeProvider.GetUtcNow(), cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (view.HasLivePending)
        {
            // The hold is cancelled rather than deleted: the card is a ledger, and a row that
            // disappears takes its own explanation with it.
            await store.AppendAsync(balance.Id, view.Program.Id, salesOrderId, actorUserId,
                LoyaltyTransactionTypes.PendingEarnCancelled, -points, false,
                "Chuyển từ chờ cộng sang đã cộng.", now, cancellationToken);
        }

        var after = await store.AppendAsync(balance.Id, view.Program.Id, salesOrderId, actorUserId,
            LoyaltyTransactionTypes.Earned, points, true,
            $"Cộng {points} điểm theo tổng giá cuối đã duyệt của đơn.", now, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return new(salesOrderId, true, points, after, LoyaltyAccrualBlock.None,
            $"Đã cộng {points} điểm.");
    }

    /// <summary>
    /// Records what the order's value has become after an adjustment, a refund or a complaint.
    ///
    /// The correction is a row of its own against the same order, so the original earning and the
    /// reason it changed both stay readable. The balance is allowed to go negative: taking points
    /// back that have already been spent is a real outcome, and pretending it floors at zero would
    /// hand out the difference. Redemption refuses while the balance is short.
    /// </summary>
    public async Task<LoyaltyAccrualResult> AdjustAsync(
        Guid salesOrderId, decimal newApprovedFinalAmount, string reason,
        Guid? actorUserId, CancellationToken cancellationToken)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw LoyaltyOperationException.Validation("Sửa điểm phải có lý do.");
        }

        var view = await store.FindAsync(salesOrderId, cancellationToken);
        if (view?.Program is null || view.Balance is null || !view.AlreadyEarned)
        {
            return new(salesOrderId, false, 0, view?.Balance?.CurrentPoints ?? 0,
                LoyaltyAccrualBlock.None, "Đơn chưa được cộng điểm nên không có gì để sửa.");
        }

        var earned = PointsFor(view.ApprovedFinalAmount, view.Program.SpendAmountPerPoint);
        var shouldBe = PointsFor(newApprovedFinalAmount, view.Program.SpendAmountPerPoint);
        var delta = shouldBe - earned;
        if (delta == 0)
        {
            return new(salesOrderId, false, 0, view.Balance.CurrentPoints,
                LoyaltyAccrualBlock.None, "Giá trị đơn không đổi; điểm giữ nguyên.");
        }

        var after = await store.AppendAsync(view.Balance.Id, view.Program.Id, salesOrderId,
            actorUserId, LoyaltyTransactionTypes.EarnedAdjusted, delta, true,
            $"Sửa điểm theo giá trị đơn sau điều chỉnh: {reason}",
            timeProvider.GetUtcNow(), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return new(salesOrderId, false, delta, after, LoyaltyAccrualBlock.None,
            $"Đã sửa {delta:+#;-#;0} điểm cho đơn.");
    }

    /// <summary>
    /// Leaves the order on the card as "Chờ cộng" and says what it is waiting for.
    ///
    /// Written once, not once per evaluation: an order sits unpaid for a credit term and every
    /// receipt or payment touch in between would otherwise add another row saying the same thing.
    /// </summary>
    private async Task<LoyaltyAccrualResult> HoldAsync(
        LoyaltyAccrualView view, int points, LoyaltyAccrualBlock block, string why,
        Guid? actorUserId, CancellationToken cancellationToken)
    {
        var balance = view.Balance?.CurrentPoints ?? 0;
        if (view.Program is null || points <= 0 || view.HasLivePending)
        {
            return new(view.SalesOrderId, false, points, balance, block, why);
        }

        var row = view.Balance
            ?? await store.EnsureBalanceAsync(view.RetailerId, timeProvider.GetUtcNow(), cancellationToken);
        await store.AppendAsync(row.Id, view.Program.Id, view.SalesOrderId, actorUserId,
            LoyaltyTransactionTypes.PendingEarn, points, false, why,
            timeProvider.GetUtcNow(), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return new(view.SalesOrderId, false, points, row.CurrentPoints, block, why);
    }

    /// <summary>
    /// Whole points only, rounded down: a part point is not a point, and rounding up would mint
    /// value the order never paid for.
    /// </summary>
    private static int PointsFor(decimal finalAmount, decimal spendPerPoint)
    {
        if (finalAmount <= 0 || spendPerPoint <= 0) return 0;
        return (int)decimal.Floor(finalAmount / spendPerPoint);
    }
}
