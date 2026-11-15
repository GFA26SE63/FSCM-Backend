using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence;

/// <summary>Region assignments are explicit grants; a group/warehouse's region label is not a grant to its siblings.</summary>
public static class ReportingScopeQueries
{
    public static IQueryable<SalesGroup> ManagedGroups(AppDbContext db, Guid userId) =>
        db.SalesGroupRecords.AsNoTracking().Where(g => g.IsActive &&
            (db.SalesGroupMemberRecords.Any(a => a.UserAccountId == userId && a.SalesGroupId == g.Id && a.IsActive)
             || (db.UserAccountRecords.Any(u => u.Id == userId && u.Role == SystemRoles.SalesManager && u.IsActive)
                 && db.UserRegionAssignmentRecords.Any(a => a.UserAccountId == userId && a.RegionId == g.RegionId && a.IsActive))));

    public static IQueryable<Warehouse> ManagedWarehouses(AppDbContext db, Guid userId) =>
        db.WarehouseRecords.AsNoTracking().Where(w =>
            db.UserWarehouseAssignmentRecords.Any(a => a.UserAccountId == userId && a.WarehouseId == w.Id && a.IsActive)
            || (db.UserAccountRecords.Any(u => u.Id == userId && u.Role == SystemRoles.WarehouseKeeper && u.IsActive)
                && db.UserRegionAssignmentRecords.Any(a => a.UserAccountId == userId && a.RegionId == w.RegionId && a.IsActive)));
}
