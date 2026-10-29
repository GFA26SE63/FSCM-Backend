using System.Data;
using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Loyalty;

public sealed class RedemptionStore(AppDbContext dbContext) : IRedemptionStore, IRedemptionWorkflowStore
{
    public async Task<RedemptionView> CreateAsync(Guid userAccountId, CreateRedemptionData data,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (data.Quantity <= 0) throw LoyaltyOperationException.Validation("Quantity must be greater than zero.");
        if (string.IsNullOrWhiteSpace(data.ClientRequestId) || data.ClientRequestId.Length > 100)
            throw LoyaltyOperationException.Validation("Client request id is required and must be at most 100 characters.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var retailer = await dbContext.RetailerRecords.SingleOrDefaultAsync(
                item => item.UserAccountId == userAccountId && item.Status == "Active", cancellationToken)
                ?? throw LoyaltyOperationException.Forbidden("The account is not linked to an active retailer.");
            var existing = await dbContext.RewardRedemptionRecords.SingleOrDefaultAsync(
                item => item.RetailerId == retailer.Id && item.ClientRequestId == data.ClientRequestId, cancellationToken);
            if (existing is not null)
            {
                var existingReward = await dbContext.LoyaltyRewardRecords.AsNoTracking()
                    .SingleAsync(item => item.Id == existing.LoyaltyRewardId, cancellationToken);
                var existingBalance = await dbContext.RetailerLoyaltyRecords.AsNoTracking()
                    .Where(item => item.RetailerId == retailer.Id).Select(item => (int?)item.CurrentPoints)
                    .SingleOrDefaultAsync(cancellationToken) ?? 0;
                return new(existing, existingReward, existingBalance);
            }

            var program = await (from link in dbContext.LoyaltyProgramRetailerRecords
                                 join item in dbContext.LoyaltyProgramRecords on link.LoyaltyProgramId equals item.Id
                                 where link.RetailerId == retailer.Id && item.IsActive && item.StartAt <= now && item.EndAt >= now
                                 orderby item.StartAt descending select item).FirstOrDefaultAsync(cancellationToken)
                ?? throw LoyaltyOperationException.Conflict("No active loyalty program applies to this retailer.");
            var reward = await dbContext.LoyaltyRewardRecords.SingleOrDefaultAsync(
                item => item.Id == data.RewardId && item.LoyaltyProgramId == program.Id && item.IsActive, cancellationToken)
                ?? throw LoyaltyOperationException.NotFound("Active loyalty reward was not found.");
            var totalPoints = checked(reward.RequiredPoints * data.Quantity);
            if (reward.AvailableQuantity.HasValue && reward.AvailableQuantity.Value < data.Quantity)
                throw LoyaltyOperationException.Conflict("The requested reward quantity is not available.");
            var balance = await dbContext.RetailerLoyaltyRecords.SingleOrDefaultAsync(
                item => item.RetailerId == retailer.Id, cancellationToken);
            if (balance is null)
            {
                balance = new RetailerLoyalty { Id = Guid.NewGuid(), RetailerId = retailer.Id,
                    CreatedAt = now, UpdatedAt = now };
                dbContext.RetailerLoyaltyRecords.Add(balance);
            }
            if (balance.CurrentPoints < totalPoints)
                throw LoyaltyOperationException.Conflict("Insufficient available loyalty points.");
            balance.CurrentPoints -= totalPoints;
            balance.HeldPoints += totalPoints;
            balance.UpdatedAt = now;
            if (reward.AvailableQuantity.HasValue) reward.AvailableQuantity -= data.Quantity;
            var transactionRow = new LoyaltyTransaction { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id,
                RetailerLoyaltyId = balance.Id, TransactionType = "RedemptionHeld", Points = -totalPoints,
                BalanceAfter = balance.CurrentPoints, Note = data.Note, CreatedAt = now };
            var redemption = new RewardRedemption { Id = Guid.NewGuid(), RetailerId = retailer.Id,
                LoyaltyRewardId = reward.Id, LoyaltyTransactionId = transactionRow.Id,
                ClientRequestId = data.ClientRequestId.Trim(), RedeemedPoints = totalPoints,
                Status = "Pending", RedeemedByUserId = userAccountId, RedeemedAt = now,
                Note = data.Note, CreatedAt = now, UpdatedAt = now };
            dbContext.LoyaltyTransactionRecords.Add(transactionRow);
            dbContext.RewardRedemptionRecords.Add(redemption);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(redemption, reward, balance.CurrentPoints);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw LoyaltyOperationException.Conflict("This redemption request was already submitted."); }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        { throw LoyaltyOperationException.Conflict("The reward inventory changed concurrently. Retry the request."); }
    }

    public async Task<IReadOnlyCollection<RedemptionView>> ListForUserAsync(Guid userAccountId, CancellationToken cancellationToken)
    {
        var retailerId = await dbContext.RetailerRecords.AsNoTracking().Where(item => item.UserAccountId == userAccountId)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw LoyaltyOperationException.Forbidden("The account is not linked to a retailer.");
        var rows = await (from redemption in dbContext.RewardRedemptionRecords.AsNoTracking()
                          join reward in dbContext.LoyaltyRewardRecords.AsNoTracking() on redemption.LoyaltyRewardId equals reward.Id
                          where redemption.RetailerId == retailerId
                          orderby redemption.CreatedAt descending
                          select new { redemption, reward }).ToArrayAsync(cancellationToken);
        var balance = await dbContext.RetailerLoyaltyRecords.AsNoTracking().Where(item => item.RetailerId == retailerId)
            .Select(item => (int?)item.CurrentPoints).SingleOrDefaultAsync(cancellationToken) ?? 0;
        return rows.Select(item => new RedemptionView(item.redemption, item.reward, balance)).ToArray();
    }

    public async Task<IReadOnlyCollection<RedemptionView>> ListForAdminAsync(CancellationToken cancellationToken)
    {
        var rows = await (from redemption in dbContext.RewardRedemptionRecords.AsNoTracking()
                          join reward in dbContext.LoyaltyRewardRecords.AsNoTracking() on redemption.LoyaltyRewardId equals reward.Id
                          join retailer in dbContext.RetailerRecords.AsNoTracking() on redemption.RetailerId equals retailer.Id
                          orderby redemption.CreatedAt descending
                          select new { redemption, reward, retailer }).ToArrayAsync(cancellationToken);
        var balances = await dbContext.RetailerLoyaltyRecords.AsNoTracking()
            .ToDictionaryAsync(item => item.RetailerId, item => item.CurrentPoints, cancellationToken);
        return rows.Select(item => new RedemptionView(item.redemption, item.reward,
            balances.GetValueOrDefault(item.retailer.Id), item.retailer.Code, item.retailer.Name)).ToArray();
    }

    public async Task<RedemptionView> TransitionAsync(Guid redemptionId, string targetStatus,
        Guid actorUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var redemption = await dbContext.RewardRedemptionRecords.SingleOrDefaultAsync(
            item => item.Id == redemptionId, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Redemption request was not found.");
        var reward = await dbContext.LoyaltyRewardRecords.SingleAsync(
            item => item.Id == redemption.LoyaltyRewardId, cancellationToken);
        var loyalty = await dbContext.RetailerLoyaltyRecords.SingleOrDefaultAsync(
            item => item.RetailerId == redemption.RetailerId, cancellationToken)
            ?? throw LoyaltyOperationException.Conflict("Retailer loyalty balance was not found.");
        if (redemption.Status == targetStatus)
            return new(redemption, reward, loyalty.CurrentPoints);
        var allowed = (redemption.Status, targetStatus) switch
        {
            ("Pending", "Approved") => true,
            ("Approved", "Delivered") => true,
            ("Delivered", "Completed") => true,
            ("Pending", "Rejected") => true,
            ("Approved", "Rejected") => true,
            _ => false
        };
        if (!allowed) throw LoyaltyOperationException.Conflict(
            $"Redemption cannot transition from {redemption.Status} to {targetStatus}.");
        if (targetStatus == "Rejected")
        {
            if (loyalty.HeldPoints < redemption.RedeemedPoints)
                throw LoyaltyOperationException.Conflict("Held loyalty points are inconsistent.");
            loyalty.HeldPoints -= redemption.RedeemedPoints;
            loyalty.CurrentPoints += redemption.RedeemedPoints;
            if (reward.AvailableQuantity.HasValue) reward.AvailableQuantity += redemption.RedeemedPoints / reward.RequiredPoints;
            dbContext.LoyaltyTransactionRecords.Add(new LoyaltyTransaction
            {
                Id = Guid.NewGuid(), LoyaltyProgramId = reward.LoyaltyProgramId,
                RetailerLoyaltyId = loyalty.Id, CreatedByUserId = actorUserId,
                TransactionType = "RedemptionRejected", Points = redemption.RedeemedPoints,
                BalanceAfter = loyalty.CurrentPoints, Note = redemption.Note, CreatedAt = now
            });
            redemption.CancelledAt = now;
        }
        else if (targetStatus == "Completed")
        {
            if (loyalty.HeldPoints < redemption.RedeemedPoints)
                throw LoyaltyOperationException.Conflict("Held loyalty points are inconsistent.");
            loyalty.HeldPoints -= redemption.RedeemedPoints;
            loyalty.LifetimeRedeemedPoints += redemption.RedeemedPoints;
            dbContext.LoyaltyTransactionRecords.Add(new LoyaltyTransaction
            {
                Id = Guid.NewGuid(), LoyaltyProgramId = reward.LoyaltyProgramId,
                RetailerLoyaltyId = loyalty.Id, CreatedByUserId = actorUserId,
                TransactionType = "RedemptionCompleted", Points = 0,
                BalanceAfter = loyalty.CurrentPoints, Note = redemption.Note, CreatedAt = now
            });
            redemption.FulfilledAt = now;
        }
        redemption.Status = targetStatus;
        redemption.UpdatedAt = now;
        loyalty.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(redemption, reward, loyalty.CurrentPoints);
    }
}
