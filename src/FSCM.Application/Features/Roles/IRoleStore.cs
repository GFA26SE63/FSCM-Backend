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
    void AddAuditLog(AuditLog audit);
    void RemoveRole(RoleDefinition role);
    void ReplacePermissions(RoleDefinition role, IEnumerable<string> newPermissionKeys, DateTimeOffset now);
    Task SaveChangesAsync(CancellationToken ct);
    Task<IReadOnlyList<ScreenApiMapping>> GetScreenApiMappingsAsync(string businessRole, CancellationToken ct);
    Task<IReadOnlyList<string>> GetApiScreenCodesAsync(string businessRole, string apiCode, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct);
}
