using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed record RunPickingScanView(QrLabelEvent Event, string QrCode);

/// <summary>Batch state a picking scan must respect: expiry and the quantity held by active locks.</summary>
public sealed record BatchPickGuard(
    string BatchCode,
    string ExpiryStatus,
    DateOnly ExpirationDate,
    decimal AvailableQuantity,
    decimal ActiveLockedQuantity);

public sealed record RunPickingWorkflowView(
    RunPickingList List,
    IReadOnlyCollection<RunPickingGather> Gathers,
    IReadOnlyCollection<RunPickingSplit> Splits,
    IReadOnlyCollection<RunPickingScanView> Scans);

public interface IRunPickingStore
{
    Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RunPickingWorkflowView>> ListAsync(
        Guid userId, string role, Guid? warehouseId, string? status, CancellationToken cancellationToken);
    Task<RunPickingWorkflowView?> FindAsync(string pickingCode, bool forUpdate, CancellationToken cancellationToken);
    Task<QrLabelEvent?> FindOperationAsync(string operationKey, CancellationToken cancellationToken);
    Task<BatchQrLabel?> FindLabelForUpdateAsync(string qrCode, CancellationToken cancellationToken);

    /// <summary>
    /// F100/F4 step 10: the expiry status, available quantity and active-lock total of a batch, so
    /// a scan onto an expired or locked batch can be blocked at pick time.
    /// </summary>
    Task<BatchPickGuard?> FindBatchPickGuardAsync(Guid batchId, CancellationToken cancellationToken);
    Task<string> NextQrCodeAsync(CancellationToken cancellationToken);

    /// <summary>F92: next shipment code from the shared sequence.</summary>
    Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken);

    /// <summary>F92: whether a shipment already exists for this (order, warehouse) pair.</summary>
    Task<bool> ShipmentExistsAsync(Guid salesOrderId, Guid warehouseId, CancellationToken cancellationToken);

    void AddLabel(BatchQrLabel label);
    void AddLabelEvent(QrLabelEvent labelEvent);
    void AddShipment(Shipment shipment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
