namespace FSCM.API.Contracts.Inventory;

public sealed record CreateGoodsReceiptRequest(Guid WarehouseId, IReadOnlyCollection<ReceiveGoodsLineRequest> Lines);
public sealed record ReceiveGoodsLineRequest(
    Guid ProductId, string? SourceLotCode, decimal Quantity,
    DateOnly ProductionDate, DateOnly ExpirationDate);
