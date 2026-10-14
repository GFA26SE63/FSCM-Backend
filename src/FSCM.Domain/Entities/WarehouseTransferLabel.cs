namespace FSCM.Domain.Entities;

public sealed class WarehouseTransferLabel
{
    public Guid Id { get; set; }
    public Guid WarehouseTransferItemId { get; set; }
    public Guid BatchQrLabelId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset DispatchedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
}
