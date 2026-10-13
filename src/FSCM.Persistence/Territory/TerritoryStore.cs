using FSCM.Application.Features.Territory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Territory;

public sealed class TerritoryStore(AppDbContext dbContext) : ITerritoryStore
{
    public async Task<IReadOnlyCollection<Region>> ListRegionsAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.RegionRecords.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.Code).ToArrayAsync(cancellationToken);
    }

    public Task<Region?> FindRegionAsync(Guid regionId, CancellationToken cancellationToken) =>
        dbContext.RegionRecords.SingleOrDefaultAsync(item => item.Id == regionId, cancellationToken);

    public Task<Region?> FindRegionByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.RegionRecords.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);

    public Task<bool> RegionHasActiveGroupsAsync(Guid regionId, CancellationToken cancellationToken) =>
        dbContext.SalesGroupRecords.AnyAsync(item => item.RegionId == regionId && item.IsActive, cancellationToken);

    public void AddRegion(Region region) => dbContext.RegionRecords.Add(region);

    public async Task<IReadOnlyCollection<SalesGroupView>> ListSalesGroupsAsync(
        Guid? regionId, bool includeInactive, CancellationToken cancellationToken)
    {
        var groups = dbContext.SalesGroupRecords.AsNoTracking().AsQueryable();
        if (regionId.HasValue) groups = groups.Where(item => item.RegionId == regionId.Value);
        if (!includeInactive) groups = groups.Where(item => item.IsActive);
        return await CreateSalesGroupViewQuery(groups.OrderBy(item => item.Code)).ToArrayAsync(cancellationToken);
    }

    public Task<SalesGroupView?> FindSalesGroupViewAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
        CreateSalesGroupViewQuery(dbContext.SalesGroupRecords.AsNoTracking().Where(item => item.Id == salesGroupId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<SalesGroup?> FindSalesGroupAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
        dbContext.SalesGroupRecords.SingleOrDefaultAsync(item => item.Id == salesGroupId, cancellationToken);

    public Task<SalesGroup?> FindSalesGroupByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.SalesGroupRecords.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);

    public async Task<bool> SalesGroupHasActiveAssignmentsAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
        await dbContext.SalesGroupMemberRecords.AnyAsync(
            item => item.SalesGroupId == salesGroupId && item.IsActive, cancellationToken) ||
        await dbContext.RetailerSalesAssignmentRecords.AnyAsync(
            item => item.SalesGroupId == salesGroupId && item.IsActive, cancellationToken);

    public void AddSalesGroup(SalesGroup salesGroup) => dbContext.SalesGroupRecords.Add(salesGroup);

    public Task<UserAccount?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserAccountRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

    public async Task<IReadOnlyCollection<SalesGroupMembershipView>> ListMembershipsAsync(
        Guid? salesGroupId, Guid? userId, bool includeInactive, CancellationToken cancellationToken)
    {
        var memberships = dbContext.SalesGroupMemberRecords.AsNoTracking().AsQueryable();
        if (salesGroupId.HasValue) memberships = memberships.Where(item => item.SalesGroupId == salesGroupId.Value);
        if (userId.HasValue) memberships = memberships.Where(item => item.UserAccountId == userId.Value);
        if (!includeInactive) memberships = memberships.Where(item => item.IsActive);
        return await (
            from membership in memberships
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on membership.SalesGroupId equals salesGroup.Id
            join user in dbContext.UserAccountRecords.AsNoTracking() on membership.UserAccountId equals user.Id
            where user.Role == SystemRoles.Sales
            orderby membership.AssignedAt descending
            select new SalesGroupMembershipView(membership, salesGroup.Code, user))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<SalesGroupMember>> ListActiveMembershipsAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await dbContext.SalesGroupMemberRecords
            .Where(item => item.UserAccountId == userId && item.IsActive)
            .ToArrayAsync(cancellationToken);

    public Task<bool> IsActiveMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken) =>
        dbContext.SalesGroupMemberRecords.AnyAsync(
            item => item.UserAccountId == userId && item.SalesGroupId == salesGroupId && item.IsActive,
            cancellationToken);

    public void AddMembership(SalesGroupMember membership) => dbContext.SalesGroupMemberRecords.Add(membership);

    public Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == retailerId, cancellationToken);

    public async Task<IReadOnlyCollection<RetailerTerritoryAssignmentView>> ListRetailerAssignmentsAsync(
        Guid? retailerId, Guid? salesGroupId, bool includeInactive, CancellationToken cancellationToken)
    {
        var assignments = dbContext.RetailerSalesAssignmentRecords.AsNoTracking().AsQueryable();
        if (retailerId.HasValue) assignments = assignments.Where(item => item.RetailerId == retailerId.Value);
        if (salesGroupId.HasValue) assignments = assignments.Where(item => item.SalesGroupId == salesGroupId.Value);
        if (!includeInactive) assignments = assignments.Where(item => item.IsActive);
        return await (
            from assignment in assignments
            join retailer in dbContext.RetailerRecords.AsNoTracking() on assignment.RetailerId equals retailer.Id
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on assignment.SalesGroupId equals salesGroup.Id
            orderby assignment.AssignedAt descending
            select new RetailerTerritoryAssignmentView(assignment, retailer, salesGroup.Code))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RetailerSalesAssignment>> ListActiveRetailerAssignmentsAsync(
        Guid retailerId, CancellationToken cancellationToken) =>
        await dbContext.RetailerSalesAssignmentRecords
            .Where(item => item.RetailerId == retailerId && item.IsActive)
            .ToArrayAsync(cancellationToken);

    public void AddRetailerAssignment(RetailerSalesAssignment assignment) =>
        dbContext.RetailerSalesAssignmentRecords.Add(assignment);

    public Task<TerritoryAccessView?> FindOrderAccessAsync(
        Guid userId, Guid retailerId, CancellationToken cancellationToken) =>
        (from membership in dbContext.SalesGroupMemberRecords.AsNoTracking()
         join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on membership.SalesGroupId equals salesGroup.Id
         join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking()
             on membership.SalesGroupId equals assignment.SalesGroupId
         join retailer in dbContext.RetailerRecords.AsNoTracking() on assignment.RetailerId equals retailer.Id
         where membership.UserAccountId == userId && membership.IsActive && salesGroup.IsActive &&
             assignment.RetailerId == retailerId && assignment.IsActive && retailer.RegionId == salesGroup.RegionId
         select new TerritoryAccessView(salesGroup.Id, salesGroup.Code))
        .SingleOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<SalesGroupView> CreateSalesGroupViewQuery(IQueryable<SalesGroup> groups) =>
        from salesGroup in groups
        join region in dbContext.RegionRecords.AsNoTracking() on salesGroup.RegionId equals region.Id
        select new SalesGroupView(salesGroup, region.Code, region.Name);
}
