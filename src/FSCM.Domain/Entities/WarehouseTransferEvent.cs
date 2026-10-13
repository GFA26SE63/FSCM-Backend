namespace FSCM.Domain.Entities;

public sealed class WarehouseTransferEvent
{
    public Guid Id { get; set; }
    public Guid WarehouseTransferId { get; set; }
    public Guid PerformedByUserId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
