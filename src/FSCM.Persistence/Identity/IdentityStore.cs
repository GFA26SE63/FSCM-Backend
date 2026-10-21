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

    public Task<UserAccount?> FindUserByEntraObjectIdAsync(
        string entraObjectId,
        CancellationToken cancellationToken) =>
        dbContext.UserAccountRecords.SingleOrDefaultAsync(
            user => user.EntraObjectId == entraObjectId,
            cancellationToken);

    public async Task<(IReadOnlyCollection<UserAccount> Items, int TotalCount)> ListUsersAsync(
        UserListQuery query,
        CancellationToken cancellationToken)
    {
        var users = dbContext.UserAccountRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            users = users.Where(user =>
                EF.Functions.ILike(user.Email, pattern) ||
                EF.Functions.ILike(user.FullName, pattern));
        }
        if (!string.IsNullOrWhiteSpace(query.Role))
            users = users.Where(user => user.Role == query.Role);
        if (query.IsActive.HasValue)
            users = users.Where(user => user.IsActive == query.IsActive.Value);

        var totalCount = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

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
        var role = await dbContext.UserAccountRecords
            .Where(user => user.Id == userId)
            .Select(user => user.Role)
            .SingleOrDefaultAsync(cancellationToken);
        var warehouseIds = role == FSCM.Domain.Authorization.SystemRoles.WarehouseKeeper
            ? await dbContext.UserWarehouseAssignmentRecords
            .Where(assignment => assignment.UserAccountId == userId && assignment.IsActive)
            .Select(assignment => assignment.WarehouseId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            : [];
        var salesGroupIds = role is FSCM.Domain.Authorization.SystemRoles.Sales or
            FSCM.Domain.Authorization.SystemRoles.SalesManager
            ? await dbContext.SalesGroupMemberRecords
            .Where(membership => membership.UserAccountId == userId && membership.IsActive)
            .Select(membership => membership.SalesGroupId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            : [];
        var assignedRegionIds = role == FSCM.Domain.Authorization.SystemRoles.SalesManager
            ? await dbContext.UserRegionAssignmentRecords
            .Where(assignment => assignment.UserAccountId == userId && assignment.IsActive)
            .Select(assignment => assignment.RegionId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            : [];
        var salesGroupRegionIds = await dbContext.SalesGroupRecords
            .Where(group => salesGroupIds.Contains(group.Id))
            .Select(group => group.RegionId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var retailerIds = role is FSCM.Domain.Authorization.SystemRoles.Sales or
            FSCM.Domain.Authorization.SystemRoles.SalesManager
            ? await dbContext.RetailerSalesAssignmentRecords
            .Where(assignment => salesGroupIds.Contains(assignment.SalesGroupId) && assignment.IsActive)
            .Select(assignment => assignment.RetailerId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            : [];

        var ownedRetailerIds = role == FSCM.Domain.Authorization.SystemRoles.Retailer
            ? await dbContext.RetailerRecords
            .Where(retailer => retailer.UserAccountId == userId)
            .Select(retailer => retailer.Id)
            .ToArrayAsync(cancellationToken)
            : [];

        var ownedRetailerRegionIds = role == FSCM.Domain.Authorization.SystemRoles.Retailer
            ? await dbContext.RetailerRecords
            .Where(retailer => retailer.UserAccountId == userId)
            .Select(retailer => retailer.RegionId)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            : [];

        return new IdentityScope(
            warehouseIds,
            assignedRegionIds.Concat(salesGroupRegionIds).Concat(ownedRetailerRegionIds).Distinct().ToArray(),
            salesGroupIds,
            retailerIds.Concat(ownedRetailerIds).Distinct().ToArray());
    }

    public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.AnyAsync(
            warehouse => warehouse.Id == warehouseId && warehouse.IsActive,
            cancellationToken);

    public Task<bool> SalesGroupExistsAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
        dbContext.SalesGroupRecords.AnyAsync(
            salesGroup => salesGroup.Id == salesGroupId && salesGroup.IsActive,
            cancellationToken);

    public Task<bool> RegionExistsAsync(Guid regionId, CancellationToken cancellationToken) =>
        dbContext.RegionRecords.AnyAsync(
            region => region.Id == regionId && region.IsActive,
            cancellationToken);

    public Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.SingleOrDefaultAsync(
            retailer => retailer.Id == retailerId,
            cancellationToken);

    public Task<Retailer?> FindRetailerByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.SingleOrDefaultAsync(
            retailer => retailer.UserAccountId == userId,
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

    public Task<UserRegionAssignment?> FindRegionAssignmentAsync(
        Guid userId,
        Guid regionId,
        CancellationToken cancellationToken) =>
        dbContext.UserRegionAssignmentRecords.SingleOrDefaultAsync(
            assignment => assignment.UserAccountId == userId && assignment.RegionId == regionId,
            cancellationToken);

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

    public void AddRegionAssignment(UserRegionAssignment assignment) =>
        dbContext.UserRegionAssignmentRecords.Add(assignment);

    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

