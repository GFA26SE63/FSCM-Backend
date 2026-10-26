using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Loyalty;

public sealed class LoyaltyStore(AppDbContext dbContext) : ILoyaltyStore
{
    public async Task<IReadOnlyCollection<LoyaltyProgram>> ListProgramsAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.LoyaltyProgramRecords.AsNoTracking();
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderByDescending(item => item.StartAt)
            .ThenBy(item => item.Code).ToArrayAsync(cancellationToken);
    }

    public Task<LoyaltyProgram?> FindProgramAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LoyaltyProgramRecords.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<string> NextProgramCodeAsync(CancellationToken cancellationToken)
    {
        var codes = await dbContext.LoyaltyProgramRecords.AsNoTracking()
            .Select(item => item.Code).ToArrayAsync(cancellationToken);
        var next = codes.Select(ParseCode).DefaultIfEmpty(0).Max() + 1;
        if (next > 999) throw LoyaltyOperationException.Conflict("Loyalty program code range is exhausted.");
        return $"LP-{next:000}";
    }

    public async Task<IReadOnlyCollection<Guid>> ListProgramRetailerIdsAsync(
        Guid programId, CancellationToken cancellationToken) =>
        await dbContext.LoyaltyProgramRetailerRecords.AsNoTracking()
            .Where(item => item.LoyaltyProgramId == programId)
            .Select(item => item.RetailerId).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> FindExistingRetailerIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.RetailerRecords.AsNoTracking().Where(item => ids.Contains(item.Id))
            .Select(item => item.Id).ToArrayAsync(cancellationToken);

    public void AddProgram(LoyaltyProgram program) => dbContext.LoyaltyProgramRecords.Add(program);

    public void AddProgramRetailers(IEnumerable<LoyaltyProgramRetailer> links) =>
        dbContext.LoyaltyProgramRetailerRecords.AddRange(links);

    public void RemoveProgramRetailers(IEnumerable<Guid> retailerIds, Guid programId) =>
        dbContext.LoyaltyProgramRetailerRecords.RemoveRange(
            dbContext.LoyaltyProgramRetailerRecords.Where(item =>
                item.LoyaltyProgramId == programId && retailerIds.Contains(item.RetailerId)));

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw LoyaltyOperationException.Conflict(
                "A concurrent request created the same loyalty program. Refresh and retry.");
        }
    }

    private static int ParseCode(string code) =>
        code.StartsWith("LP-", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(code.AsSpan(3), out var number) ? number : 0;
}
