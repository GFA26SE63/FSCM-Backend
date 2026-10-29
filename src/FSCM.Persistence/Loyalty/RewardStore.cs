using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Loyalty;

public sealed class RewardStore(AppDbContext dbContext) : IRewardStore
{
    public async Task<IReadOnlyCollection<LoyaltyReward>> ListAsync(Guid? programId, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.LoyaltyRewardRecords.AsNoTracking();
        if (programId.HasValue) query = query.Where(item => item.LoyaltyProgramId == programId.Value);
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.Code).ToArrayAsync(cancellationToken);
    }

    public Task<LoyaltyReward?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LoyaltyRewardRecords.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LoyaltyProgramRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        var codes = await dbContext.LoyaltyRewardRecords.AsNoTracking().Select(item => item.Code).ToArrayAsync(cancellationToken);
        var next = codes.Select(ParseCode).DefaultIfEmpty(0).Max() + 1;
        if (next > 999) throw LoyaltyOperationException.Conflict("Loyalty reward code range is exhausted.");
        return $"QUA-{next:000}";
    }

    public void Add(LoyaltyReward reward) => dbContext.LoyaltyRewardRecords.Add(reward);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw LoyaltyOperationException.Conflict("A concurrent request created the same loyalty reward. Refresh and retry."); }
    }

    private static int ParseCode(string code) => code.StartsWith("QUA-", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(code.AsSpan(4), out var number) ? number : 0;
}
