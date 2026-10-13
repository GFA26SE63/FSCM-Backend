using FSCM.Modules.Partners.Application;
using FSCM.Modules.Partners.Domain.Areas;
using FSCM.Modules.Partners.Domain.Retailers;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Partners.Infrastructure;

internal sealed class AreaRepository(PartnersDbContext db) : IAreaRepository
{
    public Task<Area?> GetAsync(string code, CancellationToken ct) => db.Areas.FirstOrDefaultAsync(a => a.Id == code, ct);
    public async Task<IReadOnlyList<Area>> AllAsync(CancellationToken ct) => await db.Areas.OrderBy(a => a.Id).ToListAsync(ct);
    public void Add(Area a) => db.Areas.Add(a);
    public Task<SalesGroup?> GroupAsync(string code, CancellationToken ct) => db.SalesGroups.FirstOrDefaultAsync(g => g.Id == code, ct);
    public async Task<IReadOnlyList<SalesGroup>> GroupsAsync(string? areaCode, CancellationToken ct) =>
        await db.SalesGroups.Where(g => areaCode == null || g.AreaCode == areaCode).OrderBy(g => g.Id).ToListAsync(ct);
    public Task<SalesGroup?> GroupOfMemberAsync(string userId, CancellationToken ct) => db.SalesGroups.FirstOrDefaultAsync(g => g.Members.Any(m => m.UserId == userId), ct);
    public void Add(SalesGroup g) => db.SalesGroups.Add(g);
}
internal sealed class RetailerRepository(PartnersDbContext db) : IRetailerRepository
{
    public Task<Retailer?> GetAsync(string code, CancellationToken ct) => db.Retailers.FirstOrDefaultAsync(r => r.Id == code, ct);
    public async Task<IReadOnlyList<Retailer>> SearchAsync(string? search, string? areaCode, DateTime? changedSinceUtc, CancellationToken ct)
    {
        var q = db.Retailers.AsQueryable();
        if (areaCode is not null) q = q.Where(r => r.AreaCode == areaCode);
        if (changedSinceUtc is not null) q = q.Where(r => r.UpdatedAtUtc > changedSinceUtc);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(r => r.Id.Contains(search) || r.Name.ToLower().Contains(search.ToLower()) || r.Phone.Contains(search));
        return await q.OrderBy(r => r.Id).ToListAsync(ct);
    }
    public void Add(Retailer r) => db.Retailers.Add(r);
    public Task<RetailerRegistration?> RegistrationAsync(string code, CancellationToken ct) => db.Registrations.FirstOrDefaultAsync(r => r.Id == code, ct);
    public async Task<IReadOnlyList<RetailerRegistration>> RegistrationsAsync(RegistrationStatus? status, string? declaredBy, CancellationToken ct) =>
        await db.Registrations.Where(r => (status == null || r.Status == status) && (declaredBy == null || r.DeclaredByUserId == declaredBy))
            .OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
    public void Add(RetailerRegistration r) => db.Registrations.Add(r);
}
