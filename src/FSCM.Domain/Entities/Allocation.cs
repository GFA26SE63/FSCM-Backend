// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Allocation
{
    public Guid Id { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public int AllocationSequence { get; set; }
    public decimal DistanceMeters { get; set; }
    public DateTimeOffset EstimatedArrivalAt { get; set; }
    public int RemainingLifeHoursAtArrival { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
