using FSCM.Domain.Entities;

namespace FSCM.Application.Features.QrLabels;

public sealed record QrLabelView(
    BatchQrLabel Label, Batch Batch, Warehouse Warehouse, Product Product,
    string? ParentQrCode, string RootQrCode);
public sealed record QrLabelEventView(QrLabelEvent Event, string WarehouseCode);

public interface IQrLabelStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<QrLabelView?> FindByCodeAsync(string qrCode, CancellationToken cancellationToken);
    Task<BatchQrLabel?> FindForUpdateAsync(string qrCode, CancellationToken cancellationToken);
    Task<QrLabelView?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<QrLabelView>> ListAsync(Guid userId, string role, Guid? warehouseId, Guid? batchId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<QrLabelView>> ListLineageAsync(Guid rootLabelId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<QrLabelEventView>> ListEventsAsync(Guid rootLabelId, CancellationToken cancellationToken);
    Task<QrLabelEvent?> FindEventAsync(string operationKey, CancellationToken cancellationToken);
    Task<QrLabelEvent?> FindLatestEventAsync(Guid labelId, CancellationToken cancellationToken);
    Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken);
    Task<InventoryBalance?> FindBalanceAsync(Guid batchId, CancellationToken cancellationToken);
    Task<decimal> SumActiveQuantityAsync(Guid batchId, CancellationToken cancellationToken);
    Task<string> NextQrCodeAsync(CancellationToken cancellationToken);
    void AddLabel(BatchQrLabel label);
    void AddEvent(QrLabelEvent labelEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
