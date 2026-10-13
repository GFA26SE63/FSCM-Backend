using FSCM.Application.Features.Territory;
using FSCM.Application.Features.Retailers;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Territory;

public sealed class TerritoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_region_normalizes_valid_business_code()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateRegionAsync(
            "q12", "Quận 12", "Khu vực bán hàng Quận 12", CancellationToken.None);

        Assert.Equal("Q12", result.Code);
        Assert.Equal("Quận 12", result.Name);
    }

    [Fact]
    public async Task Transfer_sales_closes_current_membership_and_preserves_history()
    {
        var fixture = new Fixture();
        var region = fixture.AddRegion("HCM");
        var source = fixture.AddGroup(region.Id, "HCM-A");
        var destination = fixture.AddGroup(region.Id, "HCM-B");
        var sales = fixture.AddUser(SystemRoles.Sales);
        var previous = fixture.AddMembership(sales.Id, source.Id, Now.AddDays(-30));

        var result = await fixture.Service.TransferSalesAsync(
            sales.Id, destination.Id, Guid.NewGuid(), true, CancellationToken.None);

        Assert.False(previous.IsActive);
        Assert.Equal(Now, previous.UnassignedAt);
        Assert.Equal(destination.Id, result.SalesGroupId);
        Assert.Equal(2, fixture.Store.Memberships.Count);
    }

    [Fact]
    public async Task Sales_manager_cannot_manage_group_outside_current_scope()
    {
        var fixture = new Fixture();
        var region = fixture.AddRegion("HCM");
        var managed = fixture.AddGroup(region.Id, "HCM-A");
        var outside = fixture.AddGroup(region.Id, "HCM-B");
        var manager = fixture.AddUser(SystemRoles.SalesManager);
        var sales = fixture.AddUser(SystemRoles.Sales);
        fixture.AddMembership(manager.Id, managed.Id, Now.AddDays(-30));

        var exception = await Assert.ThrowsAsync<TerritoryOperationException>(() =>
            fixture.Service.TransferSalesAsync(
                sales.Id, outside.Id, manager.Id, false, CancellationToken.None));

        Assert.Equal("forbidden", exception.Code);
        Assert.DoesNotContain(fixture.Store.Memberships, item => item.UserAccountId == sales.Id);
    }

    [Fact]
    public async Task Retailer_assignment_rejects_sales_group_from_another_region()
    {
        var fixture = new Fixture();
        var hcm = fixture.AddRegion("HCM");
        var bd = fixture.AddRegion("BD");
        var retailer = fixture.AddRetailer(hcm.Id);
        var group = fixture.AddGroup(bd.Id, "BD-A");

        var exception = await Assert.ThrowsAsync<TerritoryOperationException>(() =>
            fixture.Service.AssignRetailerAsync(
                retailer.Id, group.Id, Guid.NewGuid(), true, CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Order_access_requires_current_membership_and_retailer_assignment_in_same_region()
    {
        var fixture = new Fixture();
        var region = fixture.AddRegion("HCM");
        var group = fixture.AddGroup(region.Id, "HCM-A");
        var sales = fixture.AddUser(SystemRoles.Sales);
        var retailer = fixture.AddRetailer(region.Id);
        fixture.AddMembership(sales.Id, group.Id, Now.AddDays(-30));
        fixture.AddRetailerAssignment(retailer.Id, group.Id, Now.AddDays(-20));

        var allowed = await fixture.Service.CheckOrderAccessAsync(sales.Id, retailer.Id, CancellationToken.None);
        fixture.Store.Memberships.Single().IsActive = false;
        var denied = await fixture.Service.CheckOrderAccessAsync(sales.Id, retailer.Id, CancellationToken.None);

        Assert.True(allowed.CanCreateOrder);
        Assert.Equal(group.Id, allowed.SalesGroupId);
        Assert.False(denied.CanCreateOrder);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new InMemoryStore();
            Service = new TerritoryService(Store, new TestTimeProvider(Now));
        }

        public InMemoryStore Store { get; }
        public TerritoryService Service { get; }

        public Region AddRegion(string code)
        {
            var item = new Region { Id = Guid.NewGuid(), Code = code, Name = $"Khu vực {code}", IsActive = true };
            Store.Regions.Add(item);
            return item;
        }

        public SalesGroup AddGroup(Guid regionId, string code)
        {
            var item = new SalesGroup { Id = Guid.NewGuid(), RegionId = regionId, Code = code, Name = $"Nhóm {code}", IsActive = true };
            Store.Groups.Add(item);
            return item;
        }

        public UserAccount AddUser(string role)
        {
            var item = new UserAccount
            {
                Id = Guid.NewGuid(),
                Email = $"{Guid.NewGuid():N}@fscm.local",
                FullName = "Nhân viên kiểm thử",
                Role = role,
                IsActive = true
            };
            Store.Users.Add(item);
            return item;
        }

        public Retailer AddRetailer(Guid regionId)
        {
            var item = new Retailer { Id = Guid.NewGuid(), RegionId = regionId, Code = "10004127", Name = "Cửa hàng An Phú", Status = RetailerStatuses.Active };
            Store.Retailers.Add(item);
            return item;
        }

        public SalesGroupMember AddMembership(Guid userId, Guid groupId, DateTimeOffset assignedAt)
        {
            var item = new SalesGroupMember
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                SalesGroupId = groupId,
                AssignedAt = assignedAt,
                IsActive = true
            };
            Store.Memberships.Add(item);
            return item;
        }

        public RetailerSalesAssignment AddRetailerAssignment(Guid retailerId, Guid groupId, DateTimeOffset assignedAt)
        {
            var item = new RetailerSalesAssignment
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                SalesGroupId = groupId,
                AssignedAt = assignedAt,
                IsActive = true
            };
            Store.RetailerAssignments.Add(item);
            return item;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryStore : ITerritoryStore
    {
        public List<Region> Regions { get; } = [];
        public List<SalesGroup> Groups { get; } = [];
        public List<UserAccount> Users { get; } = [];
        public List<SalesGroupMember> Memberships { get; } = [];
        public List<Retailer> Retailers { get; } = [];
        public List<RetailerSalesAssignment> RetailerAssignments { get; } = [];

        public Task<IReadOnlyCollection<Region>> ListRegionsAsync(bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Region>>(Regions.Where(item => includeInactive || item.IsActive).ToArray());
        public Task<Region?> FindRegionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Regions.SingleOrDefault(item => item.Id == id));
        public Task<Region?> FindRegionByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult(Regions.SingleOrDefault(item => item.Code == code));
        public Task<bool> RegionHasActiveGroupsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Groups.Any(item => item.RegionId == id && item.IsActive));
        public void AddRegion(Region item) => Regions.Add(item);
        public Task<IReadOnlyCollection<SalesGroupView>> ListSalesGroupsAsync(Guid? regionId, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SalesGroupView>>(Groups.Where(item => (!regionId.HasValue || item.RegionId == regionId.Value) && (includeInactive || item.IsActive)).Select(ToGroupView).ToArray());
        public Task<SalesGroupView?> FindSalesGroupViewAsync(Guid id, CancellationToken cancellationToken)
        {
            var item = Groups.SingleOrDefault(value => value.Id == id);
            return Task.FromResult(item is null ? null : ToGroupView(item));
        }
        public Task<SalesGroup?> FindSalesGroupAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Groups.SingleOrDefault(item => item.Id == id));
        public Task<SalesGroup?> FindSalesGroupByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult(Groups.SingleOrDefault(item => item.Code == code));
        public Task<bool> SalesGroupHasActiveAssignmentsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Memberships.Any(item => item.SalesGroupId == id && item.IsActive) || RetailerAssignments.Any(item => item.SalesGroupId == id && item.IsActive));
        public void AddSalesGroup(SalesGroup item) => Groups.Add(item);
        public Task<UserAccount?> FindUserAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Users.SingleOrDefault(item => item.Id == id));
        public Task<IReadOnlyCollection<SalesGroupMembershipView>> ListMembershipsAsync(Guid? groupId, Guid? userId, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SalesGroupMembershipView>>(Memberships.Where(item => (!groupId.HasValue || item.SalesGroupId == groupId.Value) && (!userId.HasValue || item.UserAccountId == userId.Value) && (includeInactive || item.IsActive) && Users.Single(value => value.Id == item.UserAccountId).Role == SystemRoles.Sales).Select(item => new SalesGroupMembershipView(item, Groups.Single(value => value.Id == item.SalesGroupId).Code, Users.Single(value => value.Id == item.UserAccountId))).ToArray());
        public Task<IReadOnlyCollection<SalesGroupMember>> ListActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SalesGroupMember>>(Memberships.Where(item => item.UserAccountId == userId && item.IsActive).ToArray());
        public Task<bool> IsActiveMemberAsync(Guid userId, Guid groupId, CancellationToken cancellationToken) => Task.FromResult(Memberships.Any(item => item.UserAccountId == userId && item.SalesGroupId == groupId && item.IsActive));
        public void AddMembership(SalesGroupMember item) => Memberships.Add(item);
        public Task<Retailer?> FindRetailerAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Retailers.SingleOrDefault(item => item.Id == id));
        public Task<IReadOnlyCollection<RetailerTerritoryAssignmentView>> ListRetailerAssignmentsAsync(Guid? retailerId, Guid? groupId, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RetailerTerritoryAssignmentView>>(RetailerAssignments.Where(item => (!retailerId.HasValue || item.RetailerId == retailerId.Value) && (!groupId.HasValue || item.SalesGroupId == groupId.Value) && (includeInactive || item.IsActive)).Select(item => new RetailerTerritoryAssignmentView(item, Retailers.Single(value => value.Id == item.RetailerId), Groups.Single(value => value.Id == item.SalesGroupId).Code)).ToArray());
        public Task<IReadOnlyCollection<RetailerSalesAssignment>> ListActiveRetailerAssignmentsAsync(Guid retailerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<RetailerSalesAssignment>>(RetailerAssignments.Where(item => item.RetailerId == retailerId && item.IsActive).ToArray());
        public void AddRetailerAssignment(RetailerSalesAssignment item) => RetailerAssignments.Add(item);
        public Task<TerritoryAccessView?> FindOrderAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken)
        {
            var assignment = RetailerAssignments.SingleOrDefault(item => item.RetailerId == retailerId && item.IsActive);
            var retailer = Retailers.SingleOrDefault(item => item.Id == retailerId);
            var membership = assignment is null ? null : Memberships.SingleOrDefault(item => item.UserAccountId == userId && item.SalesGroupId == assignment.SalesGroupId && item.IsActive);
            var group = membership is null ? null : Groups.Single(item => item.Id == membership.SalesGroupId);
            return Task.FromResult(group is not null && group.IsActive && retailer!.RegionId == group.RegionId
                ? new TerritoryAccessView(group.Id, group.Code) : null);
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private SalesGroupView ToGroupView(SalesGroup item)
        {
            var region = Regions.Single(value => value.Id == item.RegionId);
            return new SalesGroupView(item, region.Code, region.Name);
        }
    }
}
