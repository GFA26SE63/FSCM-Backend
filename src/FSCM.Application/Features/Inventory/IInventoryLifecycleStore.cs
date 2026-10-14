using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Inventory;

public sealed class InventoryLifecycleView(
    Allocation allocation,
    Batch batch,
    InventoryBalance balance,
    StockHold? hold,
    Reservation? reservation)
{
    public Allocation Allocation { get; } = allocation;
    public Batch Batch { get; } = batch;
    public InventoryBalance Balance { get; } = balance;
    public StockHold? Hold { get; set; } = hold;
    public Reservation? Reservation { get; set; } = reservation;
}

public interface IInventoryLifecycleStore
{
    Task<InventoryLifecycleView?> FindForUpdateAsync(Guid allocationId, CancellationToken cancellationToken);
    void AddHold(StockHold hold);
    void AddReservation(Reservation reservation);
    void AddTransaction(InventoryTransaction transaction);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
