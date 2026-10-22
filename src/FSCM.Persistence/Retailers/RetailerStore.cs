using FSCM.Application.Features.Retailers;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Retailers;

public sealed class RetailerStore(AppDbContext dbContext) : IRetailerStore
{
    public async Task<IReadOnlyCollection<RetailerView>> ListAsync(Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken)
    {
        var retailers = dbContext.RetailerRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) retailers = retailers.Where(item => item.Status == status);
        var query =
            from retailer in retailers
            join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking().Where(item => item.IsActive) on retailer.Id equals assignment.RetailerId
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on assignment.SalesGroupId equals salesGroup.Id
            join location in dbContext.MapLocationRecords.AsNoTracking() on retailer.Id equals location.RetailerId into locations
            from location in locations.DefaultIfEmpty()
            select new { Retailer = retailer, SalesGroupId = salesGroup.Id, SalesGroupCode = salesGroup.Code, Location = location };
        if (actorRole is SystemRoles.Sales or SystemRoles.SalesManager)
        {
            query = actorRole == SystemRoles.Sales
                ? query.Where(item => item.Retailer.DeclaredByUserId == actorUserId ||
                    dbContext.SalesGroupMemberRecords.Any(member => member.UserAccountId == actorUserId && member.SalesGroupId == item.SalesGroupId && member.IsActive))
                : query.Where(item => dbContext.SalesGroupMemberRecords.Any(member => member.UserAccountId == actorUserId && member.SalesGroupId == item.SalesGroupId && member.IsActive));
        }
        return (await query.ToArrayAsync(cancellationToken))
            .Select(item => new RetailerView(item.Retailer, item.SalesGroupId, item.SalesGroupCode, item.Location))
            .OrderBy(item => item.Retailer.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken) =>
        (from retailer in dbContext.RetailerRecords.AsNoTracking()
         join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking().Where(item => item.IsActive) on retailer.Id equals assignment.RetailerId
         join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on assignment.SalesGroupId equals salesGroup.Id
         join location in dbContext.MapLocationRecords.AsNoTracking() on retailer.Id equals location.RetailerId into locations
         from location in locations.DefaultIfEmpty()
         where retailer.Id == retailerId
         select new RetailerView(retailer, salesGroup.Id, salesGroup.Code, location)).SingleOrDefaultAsync(cancellationToken);

    public Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.SingleOrDefaultAsync(item => item.Id == retailerId, cancellationToken);

    public async Task<IReadOnlyCollection<RetailerMlorView>> ListMlorDeclarationsAsync(
        IReadOnlyCollection<Guid> retailerIds, bool includeInactive, bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking
            ? dbContext.RetailerProductMlorRecords.AsQueryable()
            : dbContext.RetailerProductMlorRecords.AsNoTracking();
        query = query.Where(item => retailerIds.Contains(item.RetailerId));
        if (!includeInactive) query = query.Where(item => item.IsActive);
        var mlors = await query.OrderBy(item => item.RetailerId)
            .ThenByDescending(item => item.EffectiveFrom).ToArrayAsync(cancellationToken);
        var categoryIds = mlors.Where(item => item.ProductCategoryId.HasValue)
            .Select(item => item.ProductCategoryId!.Value).Distinct().ToArray();
        var productIds = mlors.Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value).Distinct().ToArray();
        var categories = await dbContext.ProductCategoryRecords.AsNoTracking()
            .Where(item => categoryIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var products = await dbContext.ProductRecords.AsNoTracking()
            .Where(item => productIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        return mlors.Select(item =>
        {
            categories.TryGetValue(item.ProductCategoryId ?? Guid.Empty, out var category);
            products.TryGetValue(item.ProductId ?? Guid.Empty, out var product);
            return new RetailerMlorView(item, category?.Code, category?.Name, product?.Sku, product?.Name);
        }).ToArray();
    }

    public Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.ProductCategoryRecords.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == categoryId, cancellationToken);

    public Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == productId, cancellationToken);

    public async Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductCategoryRecords.AsNoTracking().ToArrayAsync(cancellationToken);

    public Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken) =>
        dbContext.SystemConfigurationRecords.AsNoTracking().SingleOrDefaultAsync(
            item => item.ConfigKey == key && item.IsActive, cancellationToken);

    public Task<MapLocation?> FindLocationForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.MapLocationRecords.SingleOrDefaultAsync(item => item.RetailerId == retailerId, cancellationToken);

    public Task<bool> CanDeclareForGroupAsync(Guid userId, Guid regionId, Guid salesGroupId, CancellationToken cancellationToken) =>
        (from member in dbContext.SalesGroupMemberRecords.AsNoTracking()
         join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on member.SalesGroupId equals salesGroup.Id
         join user in dbContext.UserAccountRecords.AsNoTracking() on member.UserAccountId equals user.Id
         where member.UserAccountId == userId && member.SalesGroupId == salesGroupId && member.IsActive &&
             salesGroup.IsActive && salesGroup.RegionId == regionId && user.IsActive && user.Role == SystemRoles.Sales
         select member).AnyAsync(cancellationToken);

    public Task<RetailerTerritoryAccess?> FindTerritoryAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken) =>
        (from member in dbContext.SalesGroupMemberRecords.AsNoTracking()
         join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on member.SalesGroupId equals salesGroup.Id
         join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking() on member.SalesGroupId equals assignment.SalesGroupId
         join retailer in dbContext.RetailerRecords.AsNoTracking() on assignment.RetailerId equals retailer.Id
         where member.UserAccountId == userId && member.IsActive && salesGroup.IsActive && assignment.IsActive &&
             retailer.Id == retailerId && retailer.RegionId == salesGroup.RegionId
         select new RetailerTerritoryAccess(salesGroup.Id, salesGroup.Code)).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> IsActiveSalesUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserAccountRecords.AsNoTracking().AnyAsync(item => item.Id == userId && item.IsActive && item.Role == SystemRoles.Sales, cancellationToken);

    public async Task<string> NextDeclarationCodeAsync(CancellationToken cancellationToken)
    {
        var value = await dbContext.Database.SqlQueryRaw<long>("SELECT nextval('\"RetailerDeclarationCodeSequence\"') AS \"Value\"").SingleAsync(cancellationToken);
        if (value > 9999) throw RetailerOperationException.Conflict("Retailer declaration code range is exhausted.");
        return $"KB-{value:0000}";
    }

    public async Task<string> NextBusinessCodeAsync(CancellationToken cancellationToken)
    {
        var value = await dbContext.Database.SqlQueryRaw<long>("SELECT nextval('\"RetailerBusinessCodeSequence\"') AS \"Value\"").SingleAsync(cancellationToken);
        if (value > 99_999_999) throw RetailerOperationException.Conflict("Retailer business code range is exhausted.");
        return value.ToString("00000000");
    }

    public void Add(Retailer retailer) => dbContext.RetailerRecords.Add(retailer);
    public void AddAssignment(RetailerSalesAssignment assignment) => dbContext.RetailerSalesAssignmentRecords.Add(assignment);
    public void AddLocation(MapLocation location) => dbContext.MapLocationRecords.Add(location);
    public void AddMlor(RetailerProductMlor mlor) => dbContext.RetailerProductMlorRecords.Add(mlor);
    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

}
