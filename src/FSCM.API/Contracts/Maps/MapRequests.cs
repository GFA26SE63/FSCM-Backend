namespace FSCM.API.Contracts.Maps;

public sealed record GeocodeRequest(string EntityType, Guid EntityId, string Address);
public sealed record RefreshDistanceRequest(Guid WarehouseId, Guid RetailerId);
public sealed record EstimateEtaRequest(
    Guid WarehouseId,
    Guid RetailerId,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? AllocationRunAt,
    int ApprovalMinutes = 0,
    int PreparationMinutes = 0,
    DateTimeOffset? NextDispatchAt = null,
    int BufferMinutes = 0);
