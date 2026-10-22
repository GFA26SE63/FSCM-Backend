using FSCM.Domain.Entities;

namespace FSCM.Application.Features.TransitTables;

public sealed record TransitTableView(
    TransitTable Table,
    IReadOnlyCollection<TransitTableBand> Bands);

public interface ITransitTableStore
{
    Task<TransitTableView?> FindCurrentAsync(CancellationToken cancellationToken);
    Task<TransitTableView?> FindByVersionAsync(int version, CancellationToken cancellationToken);
    Task<TransitTableView?> FindEffectiveAtAsync(DateTimeOffset at, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TransitTableView>> ListAsync(CancellationToken cancellationToken);
    Task<TransitTable?> FindCurrentForUpdateAsync(CancellationToken cancellationToken);
    Task<int> MaxVersionAsync(CancellationToken cancellationToken);
    void AddTable(TransitTable table);
    void AddBands(IEnumerable<TransitTableBand> bands);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
