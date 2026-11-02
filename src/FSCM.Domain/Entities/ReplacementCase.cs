namespace FSCM.Domain.Entities;

public sealed class ReplacementCase
{
    public Guid Id { get; set; }
    public Guid ReceiptDiscrepancyId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Status { get; set; } = "Approved";
    public DateTimeOffset ApprovedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
