namespace FSCM.Modules.Logistics.Contracts;

public enum Purpose { WarehouseToRetailer = 1, WarehouseToWarehouse = 2 }

public sealed record TransitQuoteDto(decimal Km, int TransitDays, decimal TripCost, string Band);

/// <summary>Snapshot stored on the order when it goes to "Đang giao".</summary>
public sealed record DeliveryAssignmentDto(string Plate, string VehicleType, string DriverCode, string DriverName, string DriverPhone);

/// <summary>What other modules (Ordering, Inventory) may ask Logistics. No direct table access across modules.</summary>
public interface ILogisticsModule
{
    Task<TransitQuoteDto?> QuoteAsync(string fromWarehouse, string toPoint, Purpose purpose, DateOnly onDate, CancellationToken ct = default);
    Task<decimal?> DistanceKmAsync(string fromWarehouse, string toPoint, CancellationToken ct = default);
    Task<DeliveryAssignmentDto> AssignDeliveryAsync(string plate, string driverCode, int cases, CancellationToken ct = default);
    Task<(decimal DmaxRetailerKm, decimal DmaxWarehouseKm)> LimitsAsync(CancellationToken ct = default);
    /// <summary>First dispatch date for goods ordered at <paramref name="localNow"/> (Asia/Ho_Chi_Minh): next dispatch slot after now + preparation + buffer.
    /// ETA of a line = this date + transit days from the quote.</summary>
    Task<DateOnly> NextDispatchDateAsync(DateTime localNow, CancellationToken ct = default);
}
