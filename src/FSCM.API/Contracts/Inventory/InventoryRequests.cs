namespace FSCM.API.Contracts.Inventory;

public sealed record ReceiveGoodsRequest(Guid WarehouseId, IReadOnlyCollection<ReceiveGoodsLineRequest> Lines);
public sealed record ReceiveGoodsLineRequest(
    Guid ProductId, string? SourceLotCode, decimal Quantity, DateOnly ExpirationDate);
