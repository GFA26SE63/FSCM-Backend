using FSCM.Application.Features.Retailers;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Retailers;

public sealed class RetailerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Declaration_calculates_size_and_class_a_and_assigns_temporary_code()
    {
        var fixture = new Fixture();

        var result = await fixture.DeclareAsync(59_000_000, 29, 121);

        Assert.Equal("KB-0100", result.Code);
        Assert.Equal("S", result.StoreSizeCategory);
        Assert.Equal("A", result.PotentialClass);
        Assert.Equal(RetailerStatuses.PendingApproval, result.Status);
        Assert.Single(fixture.Store.Assignments);
    }

    [Fact]
    public async Task Approval_activates_retailer_and_replaces_declaration_code()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(30_000_000, 20, 60);

        var result = await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, "Hồ sơ hợp lệ", CancellationToken.None);

        Assert.Equal("10005000", result.Code);
        Assert.Equal(RetailerStatuses.Active, result.Status);
        Assert.Equal("B", result.PotentialClass);
    }

    [Fact]
    public async Task Rejection_requires_reason()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(10_000_000, 20, 20);

        var exception = await Assert.ThrowsAsync<RetailerOperationException>(() =>
            fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), false, null, CancellationToken.None));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Order_eligibility_requires_active_retailer_and_matching_territory()
    {
        var fixture = new Fixture();
        var declared = await fixture.DeclareAsync(65_000_000, 8, 100);
        var pending = await fixture.Service.CheckOrderEligibilityAsync(fixture.SalesUserId, declared.Id, CancellationToken.None);
        await fixture.Service.ReviewAsync(declared.Id, Guid.NewGuid(), true, "Hồ sơ hợp lệ", CancellationToken.None);
        fixture.Store.HasTerritoryAccess = true;

        var active = await fixture.Service.CheckOrderEligibilityAsync(fixture.SalesUserId, declared.Id, CancellationToken.None);

        Assert.False(pending.IsEligible);
        Assert.True(active.IsEligible);
    }

    private sealed class Fixture
    {
        public Guid SalesUserId { get; } = Guid.NewGuid();
        public Guid RegionId { get; } = Guid.NewGuid();
        public Guid GroupId { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public RetailerService Service { get; }

        public Fixture()
        {
            Service = new RetailerService(Store, new FixedTimeProvider(Now));
        }

        public Task<RetailerDetails> DeclareAsync(decimal revenue, decimal size, decimal capacity) =>
            Service.DeclareAsync(SalesUserId, RegionId, GroupId, "Cửa hàng thử nghiệm", "Nguyễn Văn An",
                "0909000000", null, "Thành phố Hồ Chí Minh", RetailerTypes.GroceryStore,
                revenue, size, capacity, 4, 7, "Đánh giá thử nghiệm", CancellationToken.None);
    }

    private sealed class FakeStore : IRetailerStore
    {
        public List<Retailer> Retailers { get; } = [];
        public List<RetailerSalesAssignment> Assignments { get; } = [];
        public bool HasTerritoryAccess { get; set; }

        public Task<IReadOnlyCollection<RetailerView>> ListAsync(Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RetailerView>>(Retailers.Where(item => status is null || item.Status == status).Select(ToView).ToArray());
        public Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.Where(item => item.Id == retailerId).Select(ToView).SingleOrDefault());
        public Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(Retailers.SingleOrDefault(item => item.Id == retailerId));
        public Task<bool> CanDeclareForGroupAsync(Guid userId, Guid regionId, Guid salesGroupId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<RetailerTerritoryAccess?> FindTerritoryAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(HasTerritoryAccess ? new RetailerTerritoryAccess(Assignments.Single(item => item.RetailerId == retailerId).SalesGroupId, "HCM-A") : null);
        public Task<bool> IsActiveSalesUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string> NextDeclarationCodeAsync(CancellationToken cancellationToken) => Task.FromResult("KB-0100");
        public Task<string> NextBusinessCodeAsync(CancellationToken cancellationToken) => Task.FromResult("10005000");
        public void Add(Retailer retailer) => Retailers.Add(retailer);
        public void AddAssignment(RetailerSalesAssignment assignment) => Assignments.Add(assignment);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        private RetailerView ToView(Retailer retailer) => new(retailer,
            Assignments.Single(item => item.RetailerId == retailer.Id).SalesGroupId, "HCM-A");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
