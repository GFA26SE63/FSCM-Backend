using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed class InventoryLifecycleView(
    Allocation allocation,
    Batch batch,
    InventoryBalance balance,
    Reservation? reservation)
{
    public Allocation Allocation { get; } = allocation;
    public Batch Batch { get; } = batch;
    public InventoryBalance Balance { get; } = balance;
    public Reservation? Reservation { get; set; } = reservation;
}

public interface IInventoryLifecycleStore
{
    Task<InventoryLifecycleView?> FindForUpdateAsync(Guid allocationId, CancellationToken cancellationToken);
    void AddReservation(Reservation reservation);
    void AddTransaction(InventoryTransaction transaction);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IInventoryLifecycleService
{
    Task<InventoryLifecycleResult> ReserveAsync(Guid allocationId, Guid actorUserId, int expectedVersion, CancellationToken cancellationToken);
    Task<InventoryLifecycleResult> ReleaseAsync(Guid allocationId, Guid actorUserId, int expectedVersion, CancellationToken cancellationToken);
    Task<InventoryLifecycleResult> IssueAsync(Guid allocationId, Guid actorUserId, int expectedVersion, CancellationToken cancellationToken);
}
