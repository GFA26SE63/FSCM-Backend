using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.IntegrationTests.Api;

/// <summary>API contract fixtures use isolated mapping data; live persistence tests use RoleStore.</summary>
internal sealed class ScreenMappingTestStore : IRoleStore
{
    public Task<IReadOnlyList<ScreenApiMapping>> GetScreenApiMappingsAsync(string role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScreenApiMapping>>(ScreenOperationDependencies.InitialMappings().Where(row => row.BusinessRole == role).ToArray());
    public async Task<IReadOnlyList<string>> GetApiScreenCodesAsync(string role, string apiCode, CancellationToken ct) =>
        (await GetScreenApiMappingsAsync(role, ct)).Where(row => row.ApiCode == apiCode).Select(row => row.ScreenCode).ToArray();
    public Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
    public Task<IReadOnlyList<(RoleDefinition Role, int UserCount)>> ListRolesAsync(string? search, CancellationToken ct) => throw new NotSupportedException();
    public Task<RoleDefinition?> FindByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasUsersAssignedAsync(string code, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountUsersAssignedAsync(string code, CancellationToken ct) => throw new NotSupportedException();
    public void AddRole(RoleDefinition role) => throw new NotSupportedException();
    public void AddAuditLog(AuditLog audit) => throw new NotSupportedException();
    public void RemoveRole(RoleDefinition role) => throw new NotSupportedException();
    public void ReplacePermissions(RoleDefinition role, IEnumerable<string> keys, DateTimeOffset now) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct) => throw new NotSupportedException();
}
