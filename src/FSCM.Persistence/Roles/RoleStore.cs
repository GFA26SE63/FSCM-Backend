using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Roles;

public sealed class RoleStore(AppDbContext dbContext) : IRoleStore
{
    public async Task<IReadOnlyList<ScreenApiMapping>> GetScreenApiMappingsAsync(string businessRole, CancellationToken ct) =>
        await dbContext.ScreenApiMappings.AsNoTracking().Where(row => row.BusinessRole == businessRole).ToArrayAsync(ct);

    public async Task<IReadOnlyList<string>> GetApiScreenCodesAsync(string businessRole, string apiCode, CancellationToken ct) =>
        await dbContext.ScreenApiMappings.AsNoTracking().Where(row => row.BusinessRole == businessRole && row.ApiCode == apiCode)
            .Select(row => row.ScreenCode).ToArrayAsync(ct);

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        if (dbContext.Database.CurrentTransaction is not null) return await operation();
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            var result = await operation();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task<IReadOnlyList<(RoleDefinition Role, int UserCount)>> ListRolesAsync(string? search, CancellationToken ct)
    {
        var query = dbContext.RoleDefinitionRecords
            .Include(r => r.Permissions)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.Name, pattern) ||
                EF.Functions.ILike(r.Code, pattern));
        }

        var roles = await query.ToListAsync(ct);
        var roleCodes = roles.Select(r => r.Code).ToList();

        var userCounts = await dbContext.UserAccountRecords
            .Where(u => roleCodes.Contains(u.RoleDefinition == null ? u.Role : u.RoleDefinition.Code))
            .GroupBy(u => u.RoleDefinition == null ? u.Role : u.RoleDefinition.Code)
            .Select(g => new { RoleCode = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleCode, x => x.Count, StringComparer.OrdinalIgnoreCase, ct);

        return roles
            .Select(r => (r, userCounts.TryGetValue(r.Code, out var count) ? count : 0))
            .ToList();
    }

    public Task<RoleDefinition?> FindByIdAsync(Guid roleId, CancellationToken ct) =>
        dbContext.RoleDefinitionRecords
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct) =>
        dbContext.RoleDefinitionRecords
            .AnyAsync(r => r.Code.ToLower() == code.ToLower(), ct);

    public Task<bool> HasUsersAssignedAsync(string roleCode, CancellationToken ct) =>
        dbContext.UserAccountRecords
            .AnyAsync(u => (u.RoleDefinition == null ? u.Role : u.RoleDefinition.Code).ToLower() == roleCode.ToLower(), ct);

    public Task<int> CountUsersAssignedAsync(string roleCode, CancellationToken ct) =>
        dbContext.UserAccountRecords
            .CountAsync(u => (u.RoleDefinition == null ? u.Role : u.RoleDefinition.Code).ToLower() == roleCode.ToLower(), ct);

    public void AddRole(RoleDefinition role) => dbContext.RoleDefinitionRecords.Add(role);
    public void AddAuditLog(AuditLog audit) => dbContext.AuditLogRecords.Add(audit);

    public void RemoveRole(RoleDefinition role) => dbContext.RoleDefinitionRecords.Remove(role);

    public void ReplacePermissions(RoleDefinition role, IEnumerable<string> newPermissionKeys, DateTimeOffset now)
    {
        dbContext.RolePermissionRecords.RemoveRange(role.Permissions);
        role.Permissions.Clear();
        foreach (var key in newPermissionKeys)
        {
            var p = new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PermissionKey = key,
                CreatedAt = now
            };
            role.Permissions.Add(p);
            dbContext.RolePermissionRecords.Add(p);
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => dbContext.SaveChangesAsync(ct);

}
