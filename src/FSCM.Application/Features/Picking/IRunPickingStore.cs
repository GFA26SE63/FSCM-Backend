using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed record RunPickingScanView(QrLabelEvent Event, string QrCode);

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
    Task<string> NextQrCodeAsync(CancellationToken cancellationToken);
    void AddLabel(BatchQrLabel label);
    void AddLabelEvent(QrLabelEvent labelEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
