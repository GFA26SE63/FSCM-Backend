namespace FSCM.API.Contracts.Territory;

public sealed record CreateRegionRequest(string Code, string Name, string? Description);
public sealed record UpdateRegionRequest(string Name, string? Description);
public sealed record UpdateTerritoryStatusRequest(bool IsActive);
public sealed record CreateSalesGroupRequest(Guid RegionId, string Code, string Name);
public sealed record UpdateSalesGroupRequest(string Name);
public sealed record TransferSalesRequest(Guid DestinationSalesGroupId);
public sealed record AssignRetailerTerritoryRequest(Guid DestinationSalesGroupId);
