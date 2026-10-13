using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Ordering.Domain.Allocation;
using FSCM.Modules.Ordering.Domain.Complaints;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Ordering.Domain.Picking;

namespace FSCM.Modules.Ordering.Application;

public interface IOrderingUnitOfWork : IUnitOfWork, IEventOutbox
{
    /// <summary>Forget tracked entities after a failed order in a sync batch, so the next order starts clean.</summary>
    void ClearTracked();
}

public sealed record OrderFilter(OrderStatus? Status, string? RetailerCode, string? SalesUserId, string? Search);
/// <summary>One allocation of an issued order, flattened for the sales-velocity query.</summary>
public sealed record IssuedRow(string RetailerCode, int Quantity, DateOnly IssuedOn);

public interface IOrderRepository
{
    Task<Order?> GetAsync(string code, CancellationToken ct);
    Task<Order?> GetByClientRequestAsync(Guid clientRequestId, CancellationToken ct);
    Task<IReadOnlyList<Order>> SearchAsync(OrderFilter filter, CancellationToken ct);
    Task<IReadOnlyList<IssuedRow>> IssuedAsync(string warehouse, string sku, CancellationToken ct);
    void Add(Order o);
}
public interface IPickingRepository
{
    Task<PickingList?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<PickingList>> ByOrderAsync(string orderCode, CancellationToken ct);
    Task<IReadOnlyList<PickingList>> SearchAsync(string? warehouse, PickingStatus? status, CancellationToken ct);
    void Add(PickingList p);
}
public interface IComplaintRepository
{
    Task<Complaint?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Complaint>> SearchAsync(string? retailerCode, ComplaintStatus? status, CancellationToken ct);
    void Add(Complaint c);
}
/// <summary>Weights wE / wD / wQ of the allocation score (Portal "Cấu hình hệ thống"). Dmax_retailer lives in the shared transit table (Logistics).</summary>
public interface IAllocationSettingsProvider
{
    Task<AllocationWeights> GetAsync(CancellationToken ct);
    Task SaveAsync(AllocationWeights w, CancellationToken ct);
}
