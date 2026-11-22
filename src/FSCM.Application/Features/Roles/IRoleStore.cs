using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Roles;

public interface IRoleStore
{
    Task EnsureDefaultRolesSeededAsync(TimeProvider timeProvider, CancellationToken ct);
    Task<IReadOnlyList<(RoleDefinition Role, int UserCount)>> ListRolesAsync(string? search, CancellationToken ct);
    Task<RoleDefinition?> FindByIdAsync(Guid roleId, CancellationToken ct);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct);
    Task<bool> HasUsersAssignedAsync(string roleCode, CancellationToken ct);
    Task<int> CountUsersAssignedAsync(string roleCode, CancellationToken ct);
    void AddRole(RoleDefinition role);
    void RemoveRole(RoleDefinition role);
    Task SaveChangesAsync(CancellationToken ct);
}
