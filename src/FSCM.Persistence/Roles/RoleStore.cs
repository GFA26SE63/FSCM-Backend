using FSCM.Application.Features.Roles;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Roles;

public sealed class RoleStore(AppDbContext dbContext) : IRoleStore
{
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
            .Where(u => roleCodes.Contains(u.Role))
            .GroupBy(u => u.Role)
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
            .AnyAsync(u => u.Role.ToLower() == roleCode.ToLower(), ct);

    public Task<int> CountUsersAssignedAsync(string roleCode, CancellationToken ct) =>
        dbContext.UserAccountRecords
            .CountAsync(u => u.Role.ToLower() == roleCode.ToLower(), ct);

    public void AddRole(RoleDefinition role) => dbContext.RoleDefinitionRecords.Add(role);

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

    public async Task EnsureDefaultRolesSeededAsync(TimeProvider timeProvider, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var existingCodes = await dbContext.RoleDefinitionRecords
            .Select(r => r.Code)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        var defaultRoles = new List<(string Code, string Name, string Description, bool IsSystem, string[] Permissions)>
        {
            (
                SystemRoles.Admin,
                "Quản trị viên",
                "Toàn quyền quản trị hệ thống, quản lý tài khoản và phân quyền vai trò.",
                true,
                SystemScreenKeys.All.ToArray()
            ),
            (
                SystemRoles.Operator,
                "Nhân viên vận hành",
                "Xử lý đơn hàng, điều phối giao hàng, công nợ, khiếu nại và đổi quà.",
                true,
                new[]
                {
                    SystemScreenKeys.Dashboard, SystemScreenKeys.Reports, SystemScreenKeys.WarehouseReport,
                    SystemScreenKeys.Orders, SystemScreenKeys.Allocation, SystemScreenKeys.Debts, SystemScreenKeys.Refunds,
                    SystemScreenKeys.Complaints, SystemScreenKeys.Batches, SystemScreenKeys.Readiness,
                    SystemScreenKeys.Retailers, SystemScreenKeys.Loyalty, SystemScreenKeys.Redeem, SystemScreenKeys.Kpi, SystemScreenKeys.Fleet
                }
            ),
            (
                SystemRoles.WarehouseKeeper,
                "Thủ kho",
                "Quản lý chứng từ xuất nhập kho, tồn kho, lô hàng, luân chuyển và tiêu hủy hàng hết hạn.",
                true,
                new[]
                {
                    SystemScreenKeys.Dashboard, SystemScreenKeys.WarehouseInbound, SystemScreenKeys.WarehouseOutbound,
                    SystemScreenKeys.WarehouseTransfers, SystemScreenKeys.Batches, SystemScreenKeys.WarehouseTrace, SystemScreenKeys.Disposal
                }
            ),
            (
                SystemRoles.SalesManager,
                "Quản lý bán hàng",
                "Quản lý khu vực kinh doanh, nhóm bán hàng và chỉ tiêu KPI.",
                true,
                new[]
                {
                    SystemScreenKeys.Dashboard, SystemScreenKeys.Regions, SystemScreenKeys.Kpi
                }
            ),
            (
                SystemRoles.Sales,
                "Nhân viên bán hàng",
                "Nhân viên kinh doanh khai báo khách hàng và chăm sóc đại lý trên ứng dụng di động.",
                true,
                Array.Empty<string>()
            ),
            (
                SystemRoles.Retailer,
                "Nhà bán lẻ",
                "Đại lý bán lẻ đặt hàng, thanh toán nợ và đổi quà trên ứng dụng di động.",
                true,
                Array.Empty<string>()
            )
        };

        bool changes = false;
        foreach (var def in defaultRoles)
        {
            if (!existingSet.Contains(def.Code))
            {
                var role = new RoleDefinition
                {
                    Id = Guid.NewGuid(),
                    Code = def.Code,
                    Name = def.Name,
                    Description = def.Description,
                    IsSystem = def.IsSystem,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                foreach (var perm in def.Permissions)
                {
                    role.Permissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = role.Id,
                        PermissionKey = perm,
                        CreatedAt = now
                    });
                }

                dbContext.RoleDefinitionRecords.Add(role);
                changes = true;
            }
        }

        if (changes)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }
}
