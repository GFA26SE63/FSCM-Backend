// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class GoodsReceiptLine
{
    public Guid Id { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid ProductId { get; set; }
    public string? SourceLotCode { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public DateOnly ProductionDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
