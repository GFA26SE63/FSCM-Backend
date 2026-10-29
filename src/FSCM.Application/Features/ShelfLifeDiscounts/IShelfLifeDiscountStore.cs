using FSCM.Domain.Entities;

namespace FSCM.Application.Features.ShelfLifeDiscounts;

public sealed record ShelfLifeDiscountTableView(
    ShelfLifeDiscountTable Table,
    IReadOnlyCollection<ShelfLifeDiscountTier> Tiers);

public interface IShelfLifeDiscountStore
{
    Task<ShelfLifeDiscountTableView?> FindCurrentAsync(CancellationToken cancellationToken);
    Task<ShelfLifeDiscountTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken);
    Task<ShelfLifeDiscountTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ShelfLifeDiscountTableView>> ListAsync(CancellationToken cancellationToken);
    Task<ShelfLifeDiscountTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken);
    Task<int> MaxVersionAsync(CancellationToken cancellationToken);
    void AddTable(ShelfLifeDiscountTable table);
    void AddTiers(IEnumerable<ShelfLifeDiscountTier> tiers);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
