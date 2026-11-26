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
        Assert.Equal(2, result.Permissions.Count);
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
        Assert.Equal(2, updated.Permissions.Count);
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

    private sealed class FakeRoleStore : IRoleStore
    {
        public List<RoleDefinition> Roles { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public void AddAuditLog(AuditLog audit) => Audits.Add(audit);

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
