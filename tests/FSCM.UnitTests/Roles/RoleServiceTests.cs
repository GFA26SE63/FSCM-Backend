using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Roles;

public sealed class RoleServiceTests
{
    private readonly FakeRoleStore _store = new();
    private readonly FixedTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly RoleService _service;

    public RoleServiceTests()
    {
        _service = new RoleService(_store, _timeProvider);
    }

    [Fact]
    public async Task CreateRole_fails_if_code_is_admin()
    {
        var request = new CreateRoleRequest("Admin", "Tên Admin", "Mô tả", ["dashboard"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateRoleAsync(request, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task CreateRole_succeeds_and_stores_valid_permissions()
    {
        var request = new CreateRoleRequest("CustomRole", "Vai trò tuỳ chỉnh", "Mô tả", ["dashboard", "invalid_key", "orders"]);
        var result = await _service.CreateRoleAsync(request, Guid.NewGuid(), default);

        Assert.Equal("CustomRole", result.Code);
        Assert.Equal("Vai trò tuỳ chỉnh", result.Name);
        Assert.Equal(2, result.Permissions.Count(key => !key.StartsWith("api:")));
        Assert.Contains("dashboard", result.Permissions);
        Assert.Contains("orders", result.Permissions);
        Assert.DoesNotContain("invalid_key", result.Permissions);
    }

    [Fact]
    public async Task UpdateRole_updates_info_and_replaces_permissions()
    {
        var role = new RoleDefinition
        {
            Id = Guid.NewGuid(),
            Code = "CustomRole",
            Name = "Tên cũ",
            Description = "Mô tả cũ",
            IsSystem = false,
            CreatedAt = _timeProvider.GetUtcNow(),
            UpdatedAt = _timeProvider.GetUtcNow(),
            Permissions =
            [
                new RolePermission { Id = Guid.NewGuid(), PermissionKey = "dashboard" }
            ]
        };
        _store.Roles.Add(role);

        var updateReq = new UpdateRoleRequest("Tên mới", "Mô tả mới", ["orders", "reports"]);
        var updated = await _service.UpdateRoleAsync(role.Id, updateReq, Guid.NewGuid(), default);

        Assert.Equal("Tên mới", updated.Name);
        Assert.Equal("Mô tả mới", updated.Description);
        Assert.Equal(2, updated.Permissions.Count(key => !key.StartsWith("api:")));
        Assert.Contains("orders", updated.Permissions);
        Assert.Contains("reports", updated.Permissions);
        Assert.DoesNotContain("dashboard", updated.Permissions);
    }

    [Fact]
    public async Task DeleteRole_fails_for_system_role()
    {
        var role = new RoleDefinition
        {
            Id = Guid.NewGuid(),
            Code = SystemRoles.Operator,
            Name = "Operator",
            IsSystem = true
        };
        _store.Roles.Add(role);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteRoleAsync(role.Id, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Save_removes_outbound_API_grants_even_when_a_stale_client_resubmits_them()
    {
        var role = new RoleDefinition { Id = Guid.NewGuid(), Code = "PXK_TEST", Name = "Kho",
            BaseRole = SystemRoles.WarehouseKeeper };
        _store.Roles.Add(role);
        await _service.UpdateRoleAsync(role.Id, new("Kho", null,
            ["warehouse-outbound", "warehouse-inbound", "api:PickingLists.Scan", "api:WarehouseDocuments.PrepareCartons",
                "api:WarehouseDocuments.Scan", "api:WarehouseDocuments.Factory"]), Guid.NewGuid(), default);
        var result = await _service.UpdateRoleAsync(role.Id, new("Kho", null,
            ["warehouse-inbound", "api:PickingLists.Scan", "api:WarehouseDocuments.PrepareCartons",
                "api:WarehouseDocuments.Scan", "api:WarehouseDocuments.Factory"]), Guid.NewGuid(), default);
        Assert.DoesNotContain("api:PickingLists.Scan", result.Permissions);
        Assert.DoesNotContain("api:WarehouseDocuments.PrepareCartons", result.Permissions);
        Assert.Contains("api:WarehouseDocuments.Factory", result.Permissions);
        Assert.Contains("api:WarehouseDocuments.Scan", result.Permissions); // Also used by inbound.
        Assert.DoesNotContain(role.Permissions, p => p.PermissionKey == "api:PickingLists.Scan");
        Assert.DoesNotContain("api:PickingLists.Scan", _store.Audits.Last().NewValues!);
    }

    [Fact]
    public async Task Create_discards_orphan_API_grants_and_retains_mobile_shared_grants()
    {
        var role = await _service.CreateRoleAsync(new("MOBILE_PICK", "Nhặt hàng", null,
            ["mobile-wk-picking", "api:WarehouseDocuments.Factory"], SystemRoles.WarehouseKeeper),
            Guid.NewGuid(), default);
        Assert.Contains("api:PickingLists.Scan", role.Permissions);
        Assert.DoesNotContain("api:WarehouseDocuments.Factory", role.Permissions);
    }

    [Fact]
    public async Task Mobile_gift_screen_maps_retailer_commands_without_staff_approval_commands()
    {
        var role = await _service.CreateRoleAsync(new("RT_GIFTS", "Đổi quà", null,
            BaseRole: SystemRoles.Retailer, SelectedScreenCodes: ["mobile-rt-gifts"]), Guid.NewGuid(), default);
        Assert.Contains("api:LoyaltyRedemptions.Create", role.Permissions);
        Assert.DoesNotContain("api:LoyaltyRedemptionWorkflow.Approve", role.Permissions);
    }

    [Fact]
    public async Task Explicit_empty_screen_selection_overrides_a_stale_permissions_array()
    {
        var role = new RoleDefinition { Id = Guid.NewGuid(), Code = "CLEAR", Name = "Kho", BaseRole = SystemRoles.WarehouseKeeper };
        _store.Roles.Add(role);
        var saved = await _service.UpdateRoleAsync(role.Id, new("Kho", null,
            Permissions: ["warehouse-outbound", "api:PickingLists.Scan"], SelectedScreenCodes: []), Guid.NewGuid(), default);
        Assert.Empty(saved.Permissions);
    }

    [Fact]
    public async Task Screen_only_save_adds_missing_APIs_distinctly_and_preserves_shared_mobile_API_until_last_screen_is_removed()
    {
        var created = await _service.CreateRoleAsync(new("SCREEN_UNION", "Kho", null,
            BaseRole: SystemRoles.WarehouseKeeper, SelectedScreenCodes: ["warehouse-outbound", "mobile-wk-picking"]), Guid.NewGuid(), default);
        Assert.Single(created.Permissions.Where(key => key == "api:PickingLists.Scan"));
        var mobileOnly = await _service.UpdateRoleAsync(created.Id, new("Kho", null,
            SelectedScreenCodes: ["mobile-wk-picking"]), Guid.NewGuid(), default);
        Assert.Contains("api:PickingLists.Scan", mobileOnly.Permissions);
        var cleared = await _service.UpdateRoleAsync(created.Id, new("Kho", null, SelectedScreenCodes: []), Guid.NewGuid(), default);
        Assert.Empty(cleared.Permissions);
    }

    private sealed class FakeRoleStore : IRoleStore
    {
        public List<RoleDefinition> Roles { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public void AddAuditLog(AuditLog audit) => Audits.Add(audit);
        public Task<IReadOnlyList<ScreenApiMapping>> GetScreenApiMappingsAsync(string role, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ScreenApiMapping>>(ScreenOperationDependencies.InitialMappings().Where(row => row.BusinessRole == role).ToArray());
        public async Task<IReadOnlyList<string>> GetApiScreenCodesAsync(string role, string apiCode, CancellationToken ct) =>
            (await GetScreenApiMappingsAsync(role, ct)).Where(row => row.ApiCode == apiCode).Select(row => row.ScreenCode).ToArray();
        public Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();

        public Task EnsureDefaultRolesSeededAsync(TimeProvider timeProvider, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(RoleDefinition Role, int UserCount)>> ListRolesAsync(string? search, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(RoleDefinition, int)>>(Roles.Select(r => (r, 0)).ToList());

        public Task<RoleDefinition?> FindByIdAsync(Guid roleId, CancellationToken ct) =>
            Task.FromResult(Roles.FirstOrDefault(r => r.Id == roleId));

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct) =>
            Task.FromResult(Roles.Any(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> HasUsersAssignedAsync(string roleCode, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<int> CountUsersAssignedAsync(string roleCode, CancellationToken ct) =>
            Task.FromResult(0);

        public void AddRole(RoleDefinition role) => Roles.Add(role);

        public void RemoveRole(RoleDefinition role) => Roles.Remove(role);

        public void ReplacePermissions(RoleDefinition role, IEnumerable<string> newPermissionKeys, DateTimeOffset now)
        {
            role.Permissions.Clear();
            foreach (var key in newPermissionKeys)
            {
                role.Permissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = role.Id,
                    PermissionKey = key,
                    CreatedAt = now
                });
            }
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
