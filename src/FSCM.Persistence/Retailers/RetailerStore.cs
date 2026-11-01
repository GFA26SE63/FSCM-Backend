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
            join account in dbContext.UserAccountRecords.AsNoTracking() on retailer.UserAccountId equals account.Id into accounts
            from account in accounts.DefaultIfEmpty()
            join declarationContact in dbContext.RetailerDeclarationContactRecords.AsNoTracking() on retailer.Id equals declarationContact.RetailerId into declarationContacts
            from declarationContact in declarationContacts.DefaultIfEmpty()
            join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking().Where(item => item.IsActive) on retailer.Id equals assignment.RetailerId
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on assignment.SalesGroupId equals salesGroup.Id
            join location in dbContext.MapLocationRecords.AsNoTracking() on retailer.Id equals location.RetailerId into locations
            from location in locations.DefaultIfEmpty()
            select new
            {
                Retailer = retailer,
                ContactName = account != null ? account.FullName : declarationContact!.ContactName,
                Phone = account != null ? account.Phone! : declarationContact!.Phone,
                Email = account != null ? account.Email : declarationContact!.Email,
                SalesGroupId = salesGroup.Id,
                SalesGroupCode = salesGroup.Code,
                Location = location
            };
        if (actorRole is SystemRoles.Sales or SystemRoles.SalesManager)
        {
            query = actorRole == SystemRoles.Sales
                ? query.Where(item => item.Retailer.DeclaredByUserId == actorUserId ||
                    dbContext.SalesGroupMemberRecords.Any(member => member.UserAccountId == actorUserId && member.SalesGroupId == item.SalesGroupId && member.IsActive))
                : query.Where(item => dbContext.SalesGroupMemberRecords.Any(member => member.UserAccountId == actorUserId && member.SalesGroupId == item.SalesGroupId && member.IsActive));
        }
        return (await query.ToArrayAsync(cancellationToken))
            .Select(item => new RetailerView(item.Retailer, item.ContactName, item.Phone, item.Email,
                item.SalesGroupId, item.SalesGroupCode, item.Location))
            .OrderBy(item => item.Retailer.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken)
    {
        var result = await (from retailer in dbContext.RetailerRecords.AsNoTracking()
         join account in dbContext.UserAccountRecords.AsNoTracking() on retailer.UserAccountId equals account.Id into accounts
         from account in accounts.DefaultIfEmpty()
         join declarationContact in dbContext.RetailerDeclarationContactRecords.AsNoTracking() on retailer.Id equals declarationContact.RetailerId into declarationContacts
         from declarationContact in declarationContacts.DefaultIfEmpty()
         join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking().Where(item => item.IsActive) on retailer.Id equals assignment.RetailerId
         join salesGroup in dbContext.SalesGroupRecords.AsNoTracking() on assignment.SalesGroupId equals salesGroup.Id
         join location in dbContext.MapLocationRecords.AsNoTracking() on retailer.Id equals location.RetailerId into locations
         from location in locations.DefaultIfEmpty()
         where retailer.Id == retailerId
         select new
         {
             Retailer = retailer,
             ContactName = account != null ? account.FullName : declarationContact!.ContactName,
             Phone = account != null ? account.Phone! : declarationContact!.Phone,
             Email = account != null ? account.Email : declarationContact!.Email,
             SalesGroupId = salesGroup.Id,
             SalesGroupCode = salesGroup.Code,
             Location = location
         }).SingleOrDefaultAsync(cancellationToken);
        return result is null ? null : new RetailerView(result.Retailer, result.ContactName,
            result.Phone, result.Email, result.SalesGroupId, result.SalesGroupCode, result.Location);
    }

    public Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerRecords.SingleOrDefaultAsync(item => item.Id == retailerId, cancellationToken);

    public Task<RetailerDeclarationContact?> FindDeclarationContactForUpdateAsync(
        Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.RetailerDeclarationContactRecords.SingleOrDefaultAsync(
            item => item.RetailerId == retailerId, cancellationToken);

    public Task<UserAccount?> FindUnassignedRetailerAccountAsync(
        string email, string phone, CancellationToken cancellationToken) =>
        dbContext.UserAccountRecords.SingleOrDefaultAsync(user =>
            user.Role == SystemRoles.Retailer && !user.IsActive &&
            (user.Email.ToLower() == email || user.Phone == phone) &&
            !dbContext.RetailerRecords.Any(retailer => retailer.UserAccountId == user.Id), cancellationToken);

    public async Task<Retailer?> FindDuplicateAsync(
        string name, string? phone, string? email, string? address,
        Guid? excludedRetailerId, CancellationToken cancellationToken)
    {
        var query = from item in dbContext.RetailerRecords.AsNoTracking()
            join user in dbContext.UserAccountRecords.AsNoTracking() on item.UserAccountId equals user.Id into users
            from user in users.DefaultIfEmpty()
            join declarationContact in dbContext.RetailerDeclarationContactRecords.AsNoTracking() on item.Id equals declarationContact.RetailerId into declarationContacts
            from declarationContact in declarationContacts.DefaultIfEmpty()
            where !excludedRetailerId.HasValue || item.Id != excludedRetailerId.Value
            select new { Retailer = item, User = user, DeclarationContact = declarationContact };
        var duplicate = await query.FirstOrDefaultAsync(item =>
            (phone != null && ((item.User != null && item.User.Phone == phone) ||
                (item.DeclarationContact != null && item.DeclarationContact.Phone == phone))) ||
            (email != null && ((item.User != null && item.User.Email.ToLower() == email) ||
                (item.DeclarationContact != null && item.DeclarationContact.Email.ToLower() == email))) ||
            (item.Retailer.Name.ToLower() == name && address != null && item.Retailer.Address != null &&
                item.Retailer.Address.ToLower() == address), cancellationToken);
        return duplicate?.Retailer;
    }

    public async Task<bool> ContactAccountExistsAsync(
        string phone, string email, Guid? excludedRetailerId, CancellationToken cancellationToken)
    {
        var accountExists = await dbContext.UserAccountRecords.AsNoTracking().AnyAsync(user =>
            (!excludedRetailerId.HasValue || !dbContext.RetailerRecords.Any(retailer =>
                retailer.Id == excludedRetailerId.Value && retailer.UserAccountId == user.Id)) &&
            (!excludedRetailerId.HasValue || !dbContext.RetailerDeclarationContactRecords.Any(contact =>
                contact.RetailerId == excludedRetailerId.Value &&
                (contact.Email.ToLower() == user.Email.ToLower() || contact.Phone == user.Phone))) &&
            (user.Email.ToLower() == email || (user.Phone != null && user.Phone == phone)), cancellationToken);
        return accountExists || await dbContext.RetailerDeclarationContactRecords.AsNoTracking()
            .AnyAsync(contact => contact.RetailerId != excludedRetailerId &&
                (contact.Email.ToLower() == email || contact.Phone == phone), cancellationToken);
    }

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
    public void AddContactAccount(UserAccount userAccount) => dbContext.UserAccountRecords.Add(userAccount);
    public void AddDeclarationContact(RetailerDeclarationContact contact) => dbContext.RetailerDeclarationContactRecords.Add(contact);
    public void RemoveDeclarationContact(RetailerDeclarationContact contact) => dbContext.RetailerDeclarationContactRecords.Remove(contact);
    public void AddAssignment(RetailerSalesAssignment assignment) => dbContext.RetailerSalesAssignmentRecords.Add(assignment);
    public void AddLocation(MapLocation location) => dbContext.MapLocationRecords.Add(location);
    public void AddMlor(RetailerProductMlor mlor) => dbContext.RetailerProductMlorRecords.Add(mlor);
    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

}
