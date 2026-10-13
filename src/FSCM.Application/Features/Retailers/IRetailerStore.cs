using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Retailers;

public sealed record RetailerView(Retailer Retailer, Guid SalesGroupId, string SalesGroupCode);
public sealed record RetailerTerritoryAccess(Guid SalesGroupId, string SalesGroupCode);

public interface IRetailerStore
{
    Task<IReadOnlyCollection<RetailerView>> ListAsync(Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken);
    Task<RetailerView?> FindViewAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<Retailer?> FindForUpdateAsync(Guid retailerId, CancellationToken cancellationToken);
    Task<bool> CanDeclareForGroupAsync(Guid userId, Guid regionId, Guid salesGroupId, CancellationToken cancellationToken);
    Task<RetailerTerritoryAccess?> FindTerritoryAccessAsync(Guid userId, Guid retailerId, CancellationToken cancellationToken);
    Task<bool> IsActiveSalesUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<string> NextDeclarationCodeAsync(CancellationToken cancellationToken);
    Task<string> NextBusinessCodeAsync(CancellationToken cancellationToken);
    void Add(Retailer retailer);
    void AddAssignment(RetailerSalesAssignment assignment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
