using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Territory;

public sealed record SalesGroupView(SalesGroup Group, string RegionCode, string RegionName);
public sealed record SalesGroupMembershipView(SalesGroupMember Membership, string GroupCode, UserAccount User);
public sealed record RetailerTerritoryAssignmentView(RetailerSalesAssignment Assignment, Retailer Retailer, string GroupCode);
public sealed record TerritoryAccessView(Guid SalesGroupId, string SalesGroupCode);

public interface ITerritoryStore
{
    Task<IReadOnlyCollection<Region>> ListRegionsAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<Region?> FindRegionAsync(Guid regionId, CancellationToken cancellationToken);
    Task<Region?> FindRegionByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> RegionHasActiveGroupsAsync(Guid regionId, CancellationToken cancellationToken);
    void AddRegion(Region region);

    Task<IReadOnlyCollection<SalesGroupView>> ListSalesGroupsAsync(Guid? regionId, bool includeInactive, CancellationToken cancellationToken);
    Task<SalesGroupView?> FindSalesGroupViewAsync(Guid salesGroupId, CancellationToken cancellationToken);
    Task<SalesGroup?> FindSalesGroupAsync(Guid salesGroupId, CancellationToken cancellationToken);
    Task<SalesGroup?> FindSalesGroupByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> SalesGroupHasActiveAssignmentsAsync(Guid salesGroupId, CancellationToken cancellationToken);
    void AddSalesGroup(SalesGroup salesGroup);

    Task<UserAccount?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SalesGroupMembershipView>> ListMembershipsAsync(Guid? salesGroupId, Guid? userId, bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SalesGroupMember>> ListActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> IsActiveMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken);
    void AddMembership(SalesGroupMember membership);

    Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RetailerTerritoryAssignmentView>> ListRetailerAssignmentsAsync(Guid? retailerId, Guid? salesGroupId, bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RetailerSalesAssignment>> ListActiveRetailerAssignmentsAsync(Guid retailerId, CancellationToken cancellationToken);
    void AddRetailerAssignment(RetailerSalesAssignment assignment);

    Task<TerritoryAccessView?> FindOrderAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
