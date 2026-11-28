using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Loyalty;

public sealed class LoyaltyAccrualStore(AppDbContext dbContext) : ILoyaltyAccrualStore
{
    public async Task<LoyaltyAccrualView?> FindAsync(Guid salesOrderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrderRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == salesOrderId, cancellationToken);
        if (order is null) return null;

        var balance = await dbContext.RetailerLoyaltyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RetailerId == order.RetailerId, cancellationToken);

        var ledger = balance is null
            ? []
            : await dbContext.LoyaltyTransactionRecords.AsNoTracking()
                .Where(item => item.RetailerLoyaltyId == balance.Id && item.SalesOrderId == salesOrderId)
                .OrderBy(item => item.CreatedAt)
                .Select(item => new { item.TransactionType, item.Points, item.LoyaltyProgramId,
                    item.SpendAmountPerPointSnapshot, item.CreatedAt })
                .ToArrayAsync(cancellationToken);

        var earning = ledger.FirstOrDefault(item => item.TransactionType == LoyaltyTransactionTypes.Earned);
        LoyaltyProgramSnapshot? frozenProgram = null;
        if (earning is not null)
        {
            var original = await dbContext.LoyaltyProgramRecords.AsNoTracking()
                .SingleAsync(p => p.Id == earning.LoyaltyProgramId, cancellationToken);
            var rate = earning.SpendAmountPerPointSnapshot ??
                (original.UpdatedAt <= earning.CreatedAt ? original.SpendAmountPerPoint : (decimal?)null);
            if (rate is > 0) frozenProgram = new(original.Id, rate.Value);
        }

        // A hold is live until its own cancellation lands, so both rows are counted rather than
        // just looking for the hold.
        var pending = ledger.Count(item => item.TransactionType == LoyaltyTransactionTypes.PendingEarn);
        var cancelled = ledger.Count(item => item.TransactionType == LoyaltyTransactionTypes.PendingEarnCancelled);

        // A prepaid order is settled by its payment row; a credit order is settled by the
        // receivable being collected, which is the same ledger reaching Paid.
        var payment = await dbContext.PaymentRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SalesOrderId == salesOrderId, cancellationToken);
        LoyaltyProgram? program = null;
        if (earning is null)
        {
            var capturedJson = await dbContext.AuditLogRecords.AsNoTracking()
                .Where(a => a.EntityName == nameof(SalesOrder) && a.EntityId == order.Id && a.Action == "OrderCaptured")
                .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => a.NewValues).FirstOrDefaultAsync(cancellationToken);
            var captured = OrderLoyaltyEligibility.Read(capturedJson, order.RetailerId, order.ServerReceivedAt);
            if (captured is not null)
            {
                // Expiry, pause, audience edits and rate edits after receipt do not change this entitlement.
                program = captured.BestForAmount(payment?.AdjustedAmount ?? order.FinalAmount);
            }
            else
            {
                // Legacy orders have no complete historical audience/rate snapshot. Preserve the
                // previous evaluation policy rather than inventing historical eligibility.
                var eligible = await EligibleLoyaltyPrograms.ForRetailer(dbContext, order.RetailerId, DateTimeOffset.UtcNow)
                    .AsNoTracking().ToArrayAsync(cancellationToken);
                program = LoyaltyProgramSelection.BestForAmount(eligible, payment?.AdjustedAmount ?? order.FinalAmount);
            }
        }

        return new LoyaltyAccrualView(
            salesOrderId,
            order.RetailerId,
            order.Status,
            payment?.AdjustedAmount ?? order.FinalAmount,
            payment is not null && payment.Status == PaymentStatuses.Paid,
            await dbContext.ReceiptDiscrepancyRecords.AsNoTracking().AnyAsync(
                item => item.SalesOrderId == salesOrderId && item.ResolvedAt == null, cancellationToken),
            earning is not null ? frozenProgram : program is null ? null : new LoyaltyProgramSnapshot(program.Id, program.SpendAmountPerPoint),
            balance is null
                ? null
                : new RetailerBalanceSnapshot(balance.Id, balance.CurrentPoints, balance.LifetimeEarnedPoints),
            ledger.Any(item => item.TransactionType == LoyaltyTransactionTypes.Earned),
            pending > cancelled,
            ledger.Where(item => item.TransactionType is LoyaltyTransactionTypes.Earned or
                    LoyaltyTransactionTypes.EarnedAdjusted)
                .Sum(item => item.Points))
        {
            PendingPointsForOrder = ledger.Where(item => item.TransactionType is LoyaltyTransactionTypes.PendingEarn or
                LoyaltyTransactionTypes.PendingEarnCancelled).Sum(item => item.Points),
            PendingProgramId = ledger.LastOrDefault(item => item.TransactionType == LoyaltyTransactionTypes.PendingEarn)?.LoyaltyProgramId
        };
    }

    public async Task<RetailerBalanceSnapshot> EnsureBalanceAsync(
        Guid retailerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var balance = await dbContext.RetailerLoyaltyRecords
            .SingleOrDefaultAsync(item => item.RetailerId == retailerId, cancellationToken);
        if (balance is null)
        {
            balance = new RetailerLoyalty
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                CurrentPoints = 0,
                HeldPoints = 0,
                LifetimeEarnedPoints = 0,
                LifetimeRedeemedPoints = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.RetailerLoyaltyRecords.Add(balance);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new RetailerBalanceSnapshot(balance.Id, balance.CurrentPoints, balance.LifetimeEarnedPoints);
    }

    public async Task<int> AppendAsync(
        Guid retailerLoyaltyId, Guid loyaltyProgramId, Guid? salesOrderId, Guid? actorUserId,
        string transactionType, int points, bool movesBalance, string note,
        DateTimeOffset now, CancellationToken cancellationToken, decimal? spendAmountPerPointSnapshot = null)
    {
        var balance = await dbContext.RetailerLoyaltyRecords
            .SingleAsync(item => item.Id == retailerLoyaltyId, cancellationToken);

        if (movesBalance)
        {
            balance.CurrentPoints += points;
            // Lifetime earned only ever climbs: it is a record of what was awarded, not a balance,
            // so a correction that takes points back does not rewrite the history of the award.
            if (points > 0) balance.LifetimeEarnedPoints += points;
            balance.UpdatedAt = now;
        }

        dbContext.LoyaltyTransactionRecords.Add(new LoyaltyTransaction
        {
            Id = Guid.NewGuid(),
            LoyaltyProgramId = loyaltyProgramId,
            RetailerLoyaltyId = retailerLoyaltyId,
            SalesOrderId = salesOrderId,
            CreatedByUserId = actorUserId,
            TransactionType = transactionType,
            Points = points,
            SpendAmountPerPointSnapshot = spendAmountPerPointSnapshot,
            BalanceAfter = balance.CurrentPoints,
            Note = note.Length > 500 ? note[..500] : note,
            CreatedAt = now
        });

        return balance.CurrentPoints;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
