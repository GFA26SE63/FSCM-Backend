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

        var now = DateTimeOffset.UtcNow;
        var program = await dbContext.LoyaltyProgramRecords.AsNoTracking()
            .Where(item => item.IsActive && item.StartAt <= now && item.EndAt >= now)
            .OrderByDescending(item => item.StartAt)
            .FirstOrDefaultAsync(cancellationToken);

        var balance = await dbContext.RetailerLoyaltyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RetailerId == order.RetailerId, cancellationToken);

        var ledger = balance is null
            ? []
            : await dbContext.LoyaltyTransactionRecords.AsNoTracking()
                .Where(item => item.RetailerLoyaltyId == balance.Id && item.SalesOrderId == salesOrderId)
                .Select(item => item.TransactionType)
                .ToArrayAsync(cancellationToken);

        // A hold is live until its own cancellation lands, so both rows are counted rather than
        // just looking for the hold.
        var pending = ledger.Count(type => type == LoyaltyTransactionTypes.PendingEarn);
        var cancelled = ledger.Count(type => type == LoyaltyTransactionTypes.PendingEarnCancelled);

        // Only a prepaid order is settled by its payment row; credit and COD are settled by the
        // receivable being collected, which is the same ledger reaching Paid.
        var payment = await dbContext.PaymentRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SalesOrderId == salesOrderId, cancellationToken);

        return new LoyaltyAccrualView(
            salesOrderId,
            order.RetailerId,
            order.Status,
            order.FinalAmount,
            payment is not null && payment.Status == PaymentStatuses.Paid,
            await dbContext.ReceiptDiscrepancyRecords.AsNoTracking().AnyAsync(
                item => item.SalesOrderId == salesOrderId && item.ResolvedAt == null, cancellationToken),
            program is null ? null : new LoyaltyProgramSnapshot(program.Id, program.SpendAmountPerPoint),
            balance is null
                ? null
                : new RetailerBalanceSnapshot(balance.Id, balance.CurrentPoints, balance.LifetimeEarnedPoints),
            ledger.Contains(LoyaltyTransactionTypes.Earned),
            pending > cancelled);
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
        DateTimeOffset now, CancellationToken cancellationToken)
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
            BalanceAfter = balance.CurrentPoints,
            Note = note.Length > 500 ? note[..500] : note,
            CreatedAt = now
        });

        return balance.CurrentPoints;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
