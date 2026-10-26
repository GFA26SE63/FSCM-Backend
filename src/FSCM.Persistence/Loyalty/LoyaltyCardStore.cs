using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Loyalty;

public sealed class LoyaltyCardStore(AppDbContext dbContext) : ILoyaltyCardStore
{
    public async Task<Guid> FindRetailerIdByCodeAsync(string retailerCode, CancellationToken cancellationToken) =>
        await dbContext.RetailerRecords.AsNoTracking().Where(item => item.Code == retailerCode)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken)
        ?? throw LoyaltyOperationException.NotFound("Retailer was not found.");

    public async Task<IReadOnlyCollection<LoyaltyCardData>> ListAsync(Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw LoyaltyOperationException.Forbidden("Only Admin or Operator can list retailer loyalty cards.");
        var ids = await dbContext.RetailerRecords.AsNoTracking().Where(item => item.Status == "Active")
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var results = new List<LoyaltyCardData>(ids.Length);
        foreach (var id in ids) results.Add(await GetAsync(actorUserId, actorRole, id, cancellationToken));
        return results;
    }

    public async Task<LoyaltyCardData> GetAsync(Guid actorUserId, string actorRole,
        Guid? requestedRetailerId, CancellationToken cancellationToken)
    {
        var retailerId = requestedRetailerId;
        if (actorRole == SystemRoles.Retailer)
        {
            var ownId = await dbContext.RetailerRecords.AsNoTracking()
                .Where(item => item.UserAccountId == actorUserId)
                .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken)
                ?? throw LoyaltyOperationException.Forbidden("The account is not linked to a retailer.");
            if (retailerId.HasValue && retailerId.Value != ownId)
                throw LoyaltyOperationException.Forbidden("A retailer can only view its own loyalty card.");
            retailerId = ownId;
        }
        if (!retailerId.HasValue)
            throw LoyaltyOperationException.Validation("Retailer id is required for this role.");
        var retailer = await dbContext.RetailerRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == retailerId.Value, cancellationToken)
            ?? throw LoyaltyOperationException.NotFound("Retailer was not found.");
        var balance = await dbContext.RetailerLoyaltyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RetailerId == retailer.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var program = await (from link in dbContext.LoyaltyProgramRetailerRecords.AsNoTracking()
                             join item in dbContext.LoyaltyProgramRecords.AsNoTracking() on link.LoyaltyProgramId equals item.Id
                             where link.RetailerId == retailer.Id && item.StartAt <= now && item.EndAt >= now
                             orderby item.IsActive descending, item.StartAt descending
                             select item).FirstOrDefaultAsync(cancellationToken);
        var transactions = await dbContext.LoyaltyTransactionRecords.AsNoTracking()
            .Where(item => balance != null && item.RetailerLoyaltyId == balance.Id)
            .OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        var orderIds = transactions.Where(item => item.SalesOrderId.HasValue).Select(item => item.SalesOrderId!.Value).Distinct().ToArray();
        var orders = await dbContext.SalesOrderRecords.AsNoTracking().Where(item => orderIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.OrderCode, cancellationToken);
        var transactionIds = transactions.Select(item => item.Id).ToArray();
        var redemptions = await dbContext.RewardRedemptionRecords.AsNoTracking()
            .Where(item => item.LoyaltyTransactionId.HasValue && transactionIds.Contains(item.LoyaltyTransactionId.Value))
            .ToDictionaryAsync(item => item.LoyaltyTransactionId!.Value, cancellationToken);
        var ledger = transactions.Select(item => new LoyaltyLedgerEntry(
            item.Id, item.TransactionType, item.Points, item.BalanceAfter, item.Note, item.CreatedAt,
            item.SalesOrderId, item.SalesOrderId.HasValue && orders.TryGetValue(item.SalesOrderId.Value, out var code) ? code : null,
            redemptions.TryGetValue(item.Id, out var redemption) ? redemption.Id : null,
            redemptions.TryGetValue(item.Id, out redemption) ? redemption.Status : null)).ToArray();
        return new(retailer, balance, program, ledger);
    }
}
