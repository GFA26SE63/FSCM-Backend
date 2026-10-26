using FSCM.Domain.Authorization;

namespace FSCM.Application.Features.Loyalty;

public sealed class LoyaltyCardService(ILoyaltyCardStore store)
{
    public async Task<LoyaltyCardResult> GetAsync(Guid actorUserId, string actorRole,
        Guid? requestedRetailerId, CancellationToken cancellationToken)
    {
        var data = await store.GetAsync(actorUserId, actorRole, requestedRetailerId, cancellationToken);
        var pending = data.Ledger.Where(item => item.TransactionType is "PendingEarn" or "Pending").Sum(item => item.Points);
        var expired = data.Ledger.Where(item => item.TransactionType is "Expired").Sum(item => Math.Abs(item.Points));
        return new(data.Retailer.Id, data.Retailer.Code, data.Retailer.Name,
            data.Program?.Id, data.Program?.Code, data.Program?.Name,
            data.Balance?.CurrentPoints ?? 0, data.Balance?.HeldPoints ?? 0,
            Math.Max(0, pending), expired, data.Program?.PointsExpireAt, data.Ledger);
    }

    public async Task<LoyaltyCardResult> GetByCodeAsync(Guid actorUserId, string actorRole,
        string retailerCode, CancellationToken cancellationToken) =>
        await GetAsync(actorUserId, actorRole,
            await store.FindRetailerIdByCodeAsync(retailerCode, cancellationToken), cancellationToken);

    public async Task<IReadOnlyCollection<LoyaltyCardResult>> ListAsync(Guid actorUserId, string actorRole,
        CancellationToken cancellationToken) => (await store.ListAsync(actorUserId, actorRole, cancellationToken))
        .Select(data => new LoyaltyCardResult(data.Retailer.Id, data.Retailer.Code, data.Retailer.Name,
            data.Program?.Id, data.Program?.Code, data.Program?.Name, data.Balance?.CurrentPoints ?? 0,
            data.Balance?.HeldPoints ?? 0, Math.Max(0, data.Ledger.Where(item => item.TransactionType is "PendingEarn" or "Pending").Sum(item => item.Points)),
            data.Ledger.Where(item => item.TransactionType == "Expired").Sum(item => Math.Abs(item.Points)),
            data.Program?.PointsExpireAt, data.Ledger)).ToArray();
}
