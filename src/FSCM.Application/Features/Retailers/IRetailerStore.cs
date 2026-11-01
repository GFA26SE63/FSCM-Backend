using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Retailers;

public sealed record RetailerView(
    Retailer Retailer,
    UserAccount ContactAccount,
    Guid SalesGroupId,
    string SalesGroupCode,
    MapLocation? Location);
public sealed record RetailerMlorView(
    RetailerProductMlor Mlor,
    string? CategoryCode,
    string? CategoryName,
    string? ProductSku,
    string? ProductName);
public sealed record RetailerTerritoryAccess(Guid SalesGroupId, string SalesGroupCode);

public interface IRetailerStore
{
    Task<IReadOnlyCollection<RetailerView>> ListAsync(Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken);
    Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<UserAccount?> FindContactAccountForUpdateAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<Retailer?> FindDuplicateAsync(
        string name, string? phone, string? email, string? address,
        Guid? excludedRetailerId, CancellationToken cancellationToken);
    Task<bool> ContactAccountExistsAsync(
        string phone, string email, Guid? excludedRetailerId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RetailerMlorView>> ListMlorDeclarationsAsync(
        IReadOnlyCollection<Guid> retailerIds, bool includeInactive, bool tracking,
        CancellationToken cancellationToken);
    Task<ProductCategory?> FindCategoryAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<Product?> FindProductAsync(Guid productId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken);
    Task<SystemConfiguration?> FindCurrentConfigurationAsync(string key, CancellationToken cancellationToken);
    Task<MapLocation?> FindLocationForUpdateAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<bool> CanDeclareForGroupAsync(Guid userId, Guid regionId, Guid salesGroupId, CancellationToken cancellationToken);
    Task<RetailerTerritoryAccess?> FindTerritoryAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken);
    Task<bool> IsActiveSalesUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<string> NextDeclarationCodeAsync(CancellationToken cancellationToken);
    Task<string> NextBusinessCodeAsync(CancellationToken cancellationToken);
    void Add(Retailer retailer);
    void AddContactAccount(UserAccount userAccount);
    void AddAssignment(RetailerSalesAssignment assignment);
    void AddLocation(MapLocation location);
    void AddMlor(RetailerProductMlor mlor);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
