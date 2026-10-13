namespace FSCM.Domain.Entities;

public sealed class QrLabelEvent
{
    public Guid Id { get; set; }
    public Guid BatchQrLabelId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid PerformedByUserId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
