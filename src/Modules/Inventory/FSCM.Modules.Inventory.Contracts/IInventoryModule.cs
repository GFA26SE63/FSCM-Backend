using FSCM.BuildingBlocks.Application;

namespace FSCM.Modules.Inventory.Contracts;

public sealed record AllocatableBatchDto(string BatchCode, string Warehouse, string WarehouseName, bool CentralWarehouse, DateOnly Expiry, int Available);
public sealed record HoldLine(string BatchCode, int Quantity);
public sealed record LabelInfoDto(string LabelCode, string BatchCode, string Sku, string Warehouse, int Quantity, string Status);
/// <summary>Cases taken from one label while picking (the label is split when only part of it is used).</summary>
public sealed record IssueLine(string BatchCode, string LabelCode, int Quantity);

/// <summary>Ordering asks Inventory for FEFO candidates and to hold/release/reserve/issue stock. Inventory owns batch tables.</summary>
public interface IInventoryModule
{
    Task<IReadOnlyList<AllocatableBatchDto>> AllocatableBatchesAsync(string sku, CancellationToken ct = default);
    /// <summary>Soft hold while the order waits for approval. Throws BusinessRuleException (batch.negative / concurrency) when stock is gone.</summary>
    Task HoldAsync(string orderCode, IReadOnlyList<HoldLine> lines, CancellationToken ct = default);
    Task ReleaseAsync(string orderCode, CancellationToken ct = default);
    Task ReserveAsync(string orderCode, CancellationToken ct = default);
    Task<LabelInfoDto?> GetLabelAsync(string labelCode, CancellationToken ct = default);
    /// <summary>Picking list done: reserved stock leaves the warehouse and the scanned labels are shipped / split.</summary>
    Task IssueAsync(string orderCode, string pickingCode, IReadOnlyList<IssueLine> lines, CancellationToken ct = default);
}

public sealed record RotationProposalsCreated(DateOnly RunDate, int Count) : IntegrationEvent;
public sealed record TransferCreated(string TransferCode, string Kind, string From, string To, string Sku, int Quantity) : IntegrationEvent;
public sealed record TransferReceived(string TransferCode, string ToWarehouse, string Sku, int Quantity, string ReceivedBy) : IntegrationEvent;
public sealed record BatchExpired(string BatchCode, string Warehouse, string Sku, int Quantity) : IntegrationEvent;
