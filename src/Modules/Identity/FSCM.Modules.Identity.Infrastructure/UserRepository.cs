using FSCM.Modules.Identity.Application;
using FSCM.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Identity.Infrastructure;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<AppUser?> GetAsync(string id, CancellationToken ct) => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    public Task<AppUser?> ResolveAsync(string subject, CancellationToken ct) => db.Users.FirstOrDefaultAsync(u => u.EntraObjectId == subject || u.Id == subject, ct);
    public async Task<IReadOnlyList<AppUser>> SearchAsync(string? search, UserRole? role, CancellationToken ct)
    {
        var q = db.Users.AsQueryable();
        if (role is not null) q = q.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => u.FullName.ToLower().Contains(search.ToLower()) || u.Email.ToLower().Contains(search.ToLower()) || u.Phone.Contains(search));
        return await q.OrderBy(u => u.FullName).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<AppUser>> InRoleAsync(UserRole role, string? warehouseCode, CancellationToken ct) =>
        await db.Users.Where(u => u.Active && u.Role == role && (warehouseCode == null || u.WarehouseCode == warehouseCode)).ToListAsync(ct);
    public async Task<IReadOnlyList<AppUser>> OfRetailerAsync(string retailerCode, CancellationToken ct) =>
        await db.Users.Where(u => u.Active && u.RetailerCode == retailerCode).ToListAsync(ct);
    public async Task<IReadOnlyList<AppUser>> ByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
    public void Add(AppUser u) => db.Users.Add(u);
}
