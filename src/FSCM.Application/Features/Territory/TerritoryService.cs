using System.Text.RegularExpressions;
using FSCM.Application.Features.Retailers;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Territory;

public sealed partial class TerritoryService(ITerritoryStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<RegionResult>> ListRegionsAsync(bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListRegionsAsync(includeInactive, cancellationToken)).Select(ToRegionResult).ToArray();

    public async Task<RegionResult> GetRegionAsync(Guid regionId, CancellationToken cancellationToken) =>
        ToRegionResult(await RequireRegionAsync(regionId, cancellationToken));

    public async Task<RegionResult> CreateRegionAsync(
        string code, string name, string? description, CancellationToken cancellationToken)
    {
        code = NormalizeRegionCode(code);
        (name, description) = ValidateRegionDetails(name, description);
        if (await store.FindRegionByCodeAsync(code, cancellationToken) is not null)
            throw TerritoryOperationException.Conflict($"Region code '{code}' already exists.");

        var now = timeProvider.GetUtcNow();
        var region = new Region
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddRegion(region);
        await store.SaveChangesAsync(cancellationToken);
        return ToRegionResult(region);
    }

    public async Task<RegionResult> UpdateRegionAsync(
        Guid regionId, string name, string? description, CancellationToken cancellationToken)
    {
        var region = await RequireRegionAsync(regionId, cancellationToken);
        (name, description) = ValidateRegionDetails(name, description);
        region.Name = name;
        region.Description = description;
        region.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return ToRegionResult(region);
    }

    public async Task SetRegionStatusAsync(Guid regionId, bool isActive, CancellationToken cancellationToken)
    {
        var region = await RequireRegionAsync(regionId, cancellationToken);
        if (!isActive && await store.RegionHasActiveGroupsAsync(regionId, cancellationToken))
            throw TerritoryOperationException.Conflict("A region with active sales groups cannot be inactivated.");
        region.IsActive = isActive;
        region.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<SalesGroupResult>> ListSalesGroupsAsync(
        Guid? regionId, bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListSalesGroupsAsync(regionId, includeInactive, cancellationToken)).Select(ToSalesGroupResult).ToArray();

    public async Task<SalesGroupResult> GetSalesGroupAsync(Guid salesGroupId, CancellationToken cancellationToken) =>
        ToSalesGroupResult(await RequireSalesGroupViewAsync(salesGroupId, cancellationToken));

    public async Task<SalesGroupResult> CreateSalesGroupAsync(
        Guid regionId, string code, string name, CancellationToken cancellationToken)
    {
        var region = await RequireRegionAsync(regionId, cancellationToken);
        if (!region.IsActive) throw TerritoryOperationException.Conflict("Cannot create a sales group in an inactive region.");
        code = code.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, $"^{Regex.Escape(region.Code)}-[A-Z]$", RegexOptions.CultureInvariant))
            throw TerritoryOperationException.Validation($"Sales group code must use the format {region.Code}-A.");
        name = ValidateName(name, "Sales group name");
        if (await store.FindSalesGroupByCodeAsync(code, cancellationToken) is not null)
            throw TerritoryOperationException.Conflict($"Sales group code '{code}' already exists.");

        var now = timeProvider.GetUtcNow();
        var group = new SalesGroup
        {
            Id = Guid.NewGuid(),
            RegionId = regionId,
            Code = code,
            Name = name,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddSalesGroup(group);
        await store.SaveChangesAsync(cancellationToken);
        return ToSalesGroupResult(new SalesGroupView(group, region.Code, region.Name));
    }

    public async Task<SalesGroupResult> UpdateSalesGroupAsync(
        Guid salesGroupId, string name, CancellationToken cancellationToken)
    {
        var group = await store.FindSalesGroupAsync(salesGroupId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Sales group was not found.");
        group.Name = ValidateName(name, "Sales group name");
        group.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return await GetSalesGroupAsync(salesGroupId, cancellationToken);
    }

    public async Task SetSalesGroupStatusAsync(Guid salesGroupId, bool isActive, CancellationToken cancellationToken)
    {
        var group = await store.FindSalesGroupAsync(salesGroupId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Sales group was not found.");
        if (!isActive && await store.SalesGroupHasActiveAssignmentsAsync(salesGroupId, cancellationToken))
            throw TerritoryOperationException.Conflict("A sales group with active Sales or Retailer assignments cannot be inactivated.");
        if (isActive && !(await RequireRegionAsync(group.RegionId, cancellationToken)).IsActive)
            throw TerritoryOperationException.Conflict("A sales group in an inactive region cannot be activated.");
        group.IsActive = isActive;
        group.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<SalesGroupMembershipResult>> ListMembershipsAsync(
        Guid? salesGroupId, Guid? userId, bool includeInactive,
        Guid actorUserId, bool actorIsAdmin, CancellationToken cancellationToken)
    {
        await EnsureScopedListAccessAsync(actorUserId, actorIsAdmin, salesGroupId, cancellationToken);
        return (await store.ListMembershipsAsync(salesGroupId, userId, includeInactive, cancellationToken))
            .Select(ToMembershipResult).ToArray();
    }

    public async Task<SalesGroupMembershipResult> TransferSalesAsync(
        Guid userId, Guid destinationSalesGroupId, Guid actorUserId, bool actorIsAdmin,
        CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Sales user was not found.");
        if (user.Role != SystemRoles.Sales)
            throw TerritoryOperationException.Validation("Only users with the Sales role can be assigned to a Sales group.");
        if (!user.IsActive) throw TerritoryOperationException.Conflict("An inactive Sales user cannot be assigned.");
        var destination = await store.FindSalesGroupAsync(destinationSalesGroupId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Destination sales group was not found.");
        if (!destination.IsActive) throw TerritoryOperationException.Conflict("Destination sales group is inactive.");
        await EnsureActorCanManageAsync(actorUserId, actorIsAdmin, destinationSalesGroupId, cancellationToken);

        var current = await store.ListActiveMembershipsAsync(userId, cancellationToken);
        if (current.Count == 1 && current.Single().SalesGroupId == destinationSalesGroupId)
        {
            return (await store.ListMembershipsAsync(destinationSalesGroupId, userId, false, cancellationToken))
                .Select(ToMembershipResult).Single();
        }

        if (!actorIsAdmin)
        {
            foreach (var membership in current)
                await EnsureActorCanManageAsync(actorUserId, false, membership.SalesGroupId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        foreach (var membership in current)
        {
            membership.IsActive = false;
            membership.UnassignedAt = now;
        }

        var created = new SalesGroupMember
        {
            Id = Guid.NewGuid(),
            SalesGroupId = destinationSalesGroupId,
            UserAccountId = userId,
            AssignedAt = now,
            IsActive = true
        };
        store.AddMembership(created);
        await store.SaveChangesAsync(cancellationToken);
        return new SalesGroupMembershipResult(created.Id, destination.Id, destination.Code,
            user.Id, user.Email, user.FullName, created.AssignedAt, null, true);
    }

    public async Task<IReadOnlyCollection<RetailerTerritoryAssignmentResult>> ListRetailerAssignmentsAsync(
        Guid? retailerId, Guid? salesGroupId, bool includeInactive,
        Guid actorUserId, bool actorIsAdmin, CancellationToken cancellationToken)
    {
        await EnsureScopedListAccessAsync(actorUserId, actorIsAdmin, salesGroupId, cancellationToken);
        return (await store.ListRetailerAssignmentsAsync(retailerId, salesGroupId, includeInactive, cancellationToken))
            .Select(ToRetailerAssignmentResult).ToArray();
    }

    public async Task<RetailerTerritoryAssignmentResult> AssignRetailerAsync(
        Guid retailerId, Guid destinationSalesGroupId, Guid actorUserId, bool actorIsAdmin,
        CancellationToken cancellationToken)
    {
        var retailer = await store.FindRetailerAsync(retailerId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Retailer was not found.");
        var destination = await store.FindSalesGroupAsync(destinationSalesGroupId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Destination sales group was not found.");
        if (!destination.IsActive) throw TerritoryOperationException.Conflict("Destination sales group is inactive.");
        if (destination.RegionId != retailer.RegionId)
            throw TerritoryOperationException.Validation("Retailer and Sales group must belong to the same region.");
        await EnsureActorCanManageAsync(actorUserId, actorIsAdmin, destinationSalesGroupId, cancellationToken);

        var current = await store.ListActiveRetailerAssignmentsAsync(retailerId, cancellationToken);
        if (current.Count == 1 && current.Single().SalesGroupId == destinationSalesGroupId)
        {
            return (await store.ListRetailerAssignmentsAsync(retailerId, destinationSalesGroupId, false, cancellationToken))
                .Select(ToRetailerAssignmentResult).Single();
        }

        if (!actorIsAdmin)
        {
            foreach (var assignment in current)
                await EnsureActorCanManageAsync(actorUserId, false, assignment.SalesGroupId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        foreach (var assignment in current)
        {
            assignment.IsActive = false;
            assignment.UnassignedAt = now;
        }

        var created = new RetailerSalesAssignment
        {
            Id = Guid.NewGuid(),
            RetailerId = retailerId,
            SalesGroupId = destinationSalesGroupId,
            AssignedAt = now,
            IsActive = true
        };
        store.AddRetailerAssignment(created);
        await store.SaveChangesAsync(cancellationToken);
        return new RetailerTerritoryAssignmentResult(created.Id, retailer.Id, retailer.Code, retailer.Name,
            destination.Id, destination.Code, created.AssignedAt, null, true);
    }

    public async Task<TerritoryOrderAccessResult> CheckOrderAccessAsync(
        Guid userId, Guid retailerId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken)
            ?? throw TerritoryOperationException.NotFound("Sales user was not found.");
        if (!user.IsActive || user.Role != SystemRoles.Sales)
            return new(userId, retailerId, false, null, null, "Only an active Sales user can create a Retailer order.");
        var retailer = await store.FindRetailerAsync(retailerId, cancellationToken);
        if (retailer is null)
            throw TerritoryOperationException.NotFound("Retailer was not found.");
        if (retailer.Status != RetailerStatuses.Active)
            return new(userId, retailerId, false, null, null, "Retailer must be approved and active before ordering.");

        var access = await store.FindOrderAccessAsync(userId, retailerId, cancellationToken);
        return access is null
            ? new(userId, retailerId, false, null, null, "Retailer is outside the Sales user's assigned territory.")
            : new(userId, retailerId, true, access.SalesGroupId, access.SalesGroupCode, null);
    }

    private async Task EnsureActorCanManageAsync(
        Guid actorUserId, bool actorIsAdmin, Guid salesGroupId, CancellationToken cancellationToken)
    {
        if (!actorIsAdmin && !await store.IsActiveMemberAsync(actorUserId, salesGroupId, cancellationToken))
            throw TerritoryOperationException.Forbidden("Sales Manager can only manage assignments in their own active Sales group.");
    }

    private async Task EnsureScopedListAccessAsync(
        Guid actorUserId, bool actorIsAdmin, Guid? salesGroupId, CancellationToken cancellationToken)
    {
        if (actorIsAdmin) return;
        if (!salesGroupId.HasValue ||
            !await store.IsActiveMemberAsync(actorUserId, salesGroupId.Value, cancellationToken))
            throw TerritoryOperationException.Forbidden(
                "Sales Manager must select and belong to the Sales group whose assignments are requested.");
    }

    private async Task<Region> RequireRegionAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindRegionAsync(id, cancellationToken)
        ?? throw TerritoryOperationException.NotFound("Region was not found.");

    private async Task<SalesGroupView> RequireSalesGroupViewAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindSalesGroupViewAsync(id, cancellationToken)
        ?? throw TerritoryOperationException.NotFound("Sales group was not found.");

    private static string NormalizeRegionCode(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (!RegionCodeRegex().IsMatch(code))
            throw TerritoryOperationException.Validation("Region code must contain 2 to 4 uppercase letters or digits.");
        return code;
    }

    private static (string Name, string? Description) ValidateRegionDetails(string name, string? description)
    {
        name = ValidateName(name, "Region name");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > 1000) throw TerritoryOperationException.Validation("Region description cannot exceed 1000 characters.");
        return (name, description);
    }

    private static string ValidateName(string value, string label)
    {
        value = value.Trim();
        if (value.Length is < 2 or > 150) throw TerritoryOperationException.Validation($"{label} must contain 2 to 150 characters.");
        return value;
    }

    private static RegionResult ToRegionResult(Region region) => new(
        region.Id, region.Code, region.Name, region.Description, region.IsActive, region.CreatedAt, region.UpdatedAt);

    private static SalesGroupResult ToSalesGroupResult(SalesGroupView view) => new(
        view.Group.Id, view.Group.RegionId, view.RegionCode, view.RegionName,
        view.Group.Code, view.Group.Name, view.Group.IsActive, view.Group.CreatedAt, view.Group.UpdatedAt);

    private static SalesGroupMembershipResult ToMembershipResult(SalesGroupMembershipView view) => new(
        view.Membership.Id, view.Membership.SalesGroupId, view.GroupCode,
        view.User.Id, view.User.Email, view.User.FullName, view.Membership.AssignedAt,
        view.Membership.UnassignedAt, view.Membership.IsActive);

    private static RetailerTerritoryAssignmentResult ToRetailerAssignmentResult(RetailerTerritoryAssignmentView view) => new(
        view.Assignment.Id, view.Retailer.Id, view.Retailer.Code, view.Retailer.Name,
        view.Assignment.SalesGroupId, view.GroupCode, view.Assignment.AssignedAt,
        view.Assignment.UnassignedAt, view.Assignment.IsActive);

    [GeneratedRegex("^[A-Z0-9]{2,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionCodeRegex();
}
