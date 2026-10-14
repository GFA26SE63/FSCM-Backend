using FSCM.Application.Features.Identity;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Identity;

public sealed class IdentityStore(AppDbContext dbContext) : IIdentityStore
{
    public Task<UserAccount?> FindUserByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        dbContext.UserAccountRecords.SingleOrDefaultAsync(
            user => user.Email == normalizedEmail,
            cancellationToken);

    public Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserAccountRecords.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserCredentials.SingleOrDefaultAsync(
            credential => credential.UserAccountId == userId,
            cancellationToken);

    public Task<RefreshToken?> FindRefreshTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken) =>
        dbContext.RefreshTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash,
            cancellationToken);

    public Task<AccountToken?> FindAccountTokenAsync(
        string tokenHash,
        string purpose,
        CancellationToken cancellationToken) =>
        dbContext.AccountTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash && token.Purpose == purpose,
            cancellationToken);

    public async Task<IdentityScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var warehouseIds = await dbContext.UserWarehouseAssignmentRecords
            .Where(assignment => assignment.UserAccountId == userId && assignment.IsActive)
            .Select(assignment => assignment.WarehouseId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var salesGroupIds = await dbContext.SalesGroupMemberRecords
            .Where(membership => membership.UserAccountId == userId && membership.IsActive)
            .Select(membership => membership.SalesGroupId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var retailerIds = await dbContext.RetailerSalesAssignmentRecords
            .Where(assignment => salesGroupIds.Contains(assignment.SalesGroupId) && assignment.IsActive)
            .Select(assignment => assignment.RetailerId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return new IdentityScope(warehouseIds, salesGroupIds, retailerIds);
    }

    public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.AnyAsync(
            warehouse => warehouse.Id == warehouseId && warehouse.IsActive,
            cancellationToken);

    public Task<bool> SalesGroupExistsAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
        dbContext.SalesGroupRecords.AnyAsync(
            salesGroup => salesGroup.Id == salesGroupId && salesGroup.IsActive,
            cancellationToken);

    public Task<UserWarehouseAssignment?> FindWarehouseAssignmentAsync(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken) =>
        dbContext.UserWarehouseAssignmentRecords.SingleOrDefaultAsync(
            assignment => assignment.UserAccountId == userId && assignment.WarehouseId == warehouseId,
            cancellationToken);

    public Task<SalesGroupMember?> FindSalesGroupMembershipAsync(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken) =>
        dbContext.SalesGroupMemberRecords.SingleOrDefaultAsync(
            membership => membership.UserAccountId == userId &&
                membership.SalesGroupId == salesGroupId &&
                membership.IsActive,
            cancellationToken);

    public async Task<IReadOnlyCollection<SalesGroupMember>> ListActiveSalesGroupMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.SalesGroupMemberRecords
            .Where(membership => membership.UserAccountId == userId && membership.IsActive)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<RefreshToken>> GetActiveRefreshTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            .Where(token => token.UserAccountId == userId && token.RevokedAt == null && token.ExpiresAt > now)
            .ToArrayAsync(cancellationToken);

    public void AddUser(UserAccount user) => dbContext.UserAccountRecords.Add(user);

    public void AddCredential(UserCredential credential) => dbContext.UserCredentials.Add(credential);

    public void AddRefreshToken(RefreshToken refreshToken) => dbContext.RefreshTokens.Add(refreshToken);

    public void AddAccountToken(AccountToken accountToken) => dbContext.AccountTokens.Add(accountToken);

    public void AddWarehouseAssignment(UserWarehouseAssignment assignment) =>
        dbContext.UserWarehouseAssignmentRecords.Add(assignment);

    public void AddSalesGroupMembership(SalesGroupMember membership) =>
        dbContext.SalesGroupMemberRecords.Add(membership);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

