using System.Security.Cryptography;
using System.Text;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Deliveries;

public sealed class DeliveryService(
    IDeliveryStore store,
    IPaymentPlanService paymentPlanService,
    IPaymentStore paymentStore,
    IInventoryLifecycleService inventoryLifecycleService,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<DriverResult>> ListDriversAsync(
        string actorRole, bool includeInactive, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        return (await store.ListDriversAsync(includeInactive, cancellationToken))
            .Select(ToResult).ToArray();
    }

    public async Task<IReadOnlyCollection<VehicleResult>> ListVehiclesAsync(
        string actorRole, bool includeInactive, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        return (await store.ListVehiclesAsync(includeInactive, cancellationToken))
            .Select(ToResult).ToArray();
    }

    public async Task<DriverResult> CreateDriverAsync(
        CreateDriverData data, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        if (data.WarehouseId == Guid.Empty || !await store.WarehouseExistsAsync(data.WarehouseId, cancellationToken))
            throw DeliveryOperationException.Validation("A valid warehouse is required.");
        var fullName = NormalizeRequired(data.FullName, "Driver name", 150);
        var phone = NormalizeRequired(data.Phone, "Driver phone", 30);
        var licenseNumber = NormalizeRequired(data.LicenseNumber, "Driver license number", 80);
        var code = string.IsNullOrWhiteSpace(data.Code)
            ? await store.NextDriverCodeAsync(cancellationToken)
            : NormalizeRequired(data.Code, "Driver code", 20).ToUpperInvariant();
        if (await store.DriverCodeExistsAsync(code, null, cancellationToken))
            throw DeliveryOperationException.Conflict($"Driver code '{code}' already exists.");

        var now = timeProvider.GetUtcNow();
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            WarehouseId = data.WarehouseId,
            Code = code,
            FullName = fullName,
            Phone = phone,
            LicenseNumber = licenseNumber,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddDriver(driver);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(driver);
    }

    public async Task<DriverResult> UpdateDriverAsync(
        Guid id, UpdateDriverData data, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        var driver = await store.FindDriverForUpdateAsync(id, cancellationToken)
            ?? throw DeliveryOperationException.NotFound("Driver was not found.");
        driver.FullName = NormalizeRequired(data.FullName, "Driver name", 150);
        driver.Phone = NormalizeRequired(data.Phone, "Driver phone", 30);
        driver.LicenseNumber = NormalizeRequired(data.LicenseNumber, "Driver license number", 80);
        driver.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(driver);
    }

    public async Task<DriverResult> SetDriverActiveAsync(
        Guid id, bool isActive, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        var driver = await store.FindDriverForUpdateAsync(id, cancellationToken)
            ?? throw DeliveryOperationException.NotFound("Driver was not found.");
        if (driver.IsActive != isActive)
        {
            driver.IsActive = isActive;
            driver.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
        }
        return ToResult(driver);
    }

    public async Task<VehicleResult> CreateVehicleAsync(
        CreateVehicleData data, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        if (data.WarehouseId == Guid.Empty || !await store.WarehouseExistsAsync(data.WarehouseId, cancellationToken))
            throw DeliveryOperationException.Validation("A valid warehouse is required.");
        var plateNumber = NormalizeRequired(data.PlateNumber, "Plate number", 30).ToUpperInvariant();
        var vehicleType = NormalizeRequired(data.VehicleType, "Vehicle type", 50);
        ValidateCapacity(data.Capacity);
        if (await store.VehiclePlateExistsAsync(plateNumber, null, cancellationToken))
            throw DeliveryOperationException.Conflict($"Plate number '{plateNumber}' already exists.");

        var now = timeProvider.GetUtcNow();
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            WarehouseId = data.WarehouseId,
            PlateNumber = plateNumber,
            VehicleType = vehicleType,
            Capacity = data.Capacity,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddVehicle(vehicle);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(vehicle);
    }

    public async Task<VehicleResult> UpdateVehicleAsync(
        Guid id, UpdateVehicleData data, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        var vehicle = await store.FindVehicleForUpdateAsync(id, cancellationToken)
            ?? throw DeliveryOperationException.NotFound("Vehicle was not found.");
        vehicle.VehicleType = NormalizeRequired(data.VehicleType, "Vehicle type", 50);
        ValidateCapacity(data.Capacity);
        vehicle.Capacity = data.Capacity;
        vehicle.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(vehicle);
    }

    public async Task<VehicleResult> SetVehicleActiveAsync(
        Guid id, bool isActive, string actorRole, CancellationToken cancellationToken)
    {
        EnsureOperator(actorRole);
        var vehicle = await store.FindVehicleForUpdateAsync(id, cancellationToken)
            ?? throw DeliveryOperationException.NotFound("Vehicle was not found.");
        if (vehicle.IsActive != isActive)
        {
            vehicle.IsActive = isActive;
            vehicle.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveChangesAsync(cancellationToken);
        }
        return ToResult(vehicle);
    }

    public async Task<IReadOnlyCollection<DeliveryResult>> ListAsync(
        Guid actorUserId, string actorRole, string? status, CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        if (!string.IsNullOrWhiteSpace(status) &&
            status is not (DeliveryStatuses.Delivering or DeliveryStatuses.Delivered))
            throw DeliveryOperationException.Validation("Delivery status filter is invalid.");
        return (await store.ListAsync(actorUserId, actorRole, status, cancellationToken))
            .Select(ToResult).ToArray();
    }

    public async Task<DeliveryResult> GetByOrderAsync(
        Guid orderId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        ValidateReadRole(actorRole);
        var view = await store.FindByOrderAsync(orderId, false, cancellationToken)
            ?? throw DeliveryOperationException.NotFound("Delivery was not found.");
        if (!await store.CanViewAsync(view, actorUserId, actorRole, cancellationToken))
            throw DeliveryOperationException.Forbidden("You cannot view this delivery.");
        return ToResult(view);
    }


    private static DriverResult ToResult(Driver item) =>
        new(item.Id, item.WarehouseId, item.Code, item.FullName, item.Phone, item.LicenseNumber, item.IsActive);

    private static VehicleResult ToResult(Vehicle item) =>
        new(item.Id, item.WarehouseId, item.PlateNumber, item.VehicleType, item.Capacity, item.IsActive);

    private static DeliveryResult ToResult(DeliveryView view) => new(
        view.Delivery.Id, view.Order.Id, view.Order.OrderCode, view.Retailer.Id,
        view.Order.RetailerCodeSnapshot, view.Order.RetailerNameSnapshot,
        view.Order.Status, view.Order.Version, view.Delivery.DriverId,
        view.Delivery.DriverCodeSnapshot, view.Delivery.DriverNameSnapshot, view.Delivery.DriverPhoneSnapshot,
        view.Delivery.DriverLicenseSnapshot, view.Delivery.VehicleId,
        view.Delivery.PlateNumberSnapshot, view.Delivery.VehicleTypeSnapshot,
        view.Delivery.Status, view.Delivery.Version, view.Delivery.AssignedAt,
        view.Delivery.DispatchedAt, view.Delivery.DeliveredAt, view.Delivery.Note,
        view.Receipt is null ? null : new ReceiptConfirmationResult(
            view.Receipt.Id, view.Receipt.ConfirmedByRetailerUserId, view.Receipt.Status,
            view.Receipt.ConfirmedAt, view.Receipt.Note,
            view.ReceiptItems.Select(item => new ReceiptItemResult(
                item.Item.Id, item.Item.BatchId, item.Item.BatchQrLabelId, item.QrCode,
                item.Item.ExpectedQuantity, item.Item.ReceivedQuantity, item.Item.ScannedAt)).ToArray()));

    private static void EnsureOperator(string actorRole)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator))
            throw DeliveryOperationException.Forbidden("Only Admin or Operator can dispatch orders or view delivery master data.");
    }

    private static void ValidateReadRole(string role)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager or
            SystemRoles.Sales or SystemRoles.WarehouseKeeper or SystemRoles.Retailer))
            throw DeliveryOperationException.Forbidden("Role cannot view deliveries.");
    }

    private static string NormalizeOperationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw DeliveryOperationException.Validation("Idempotency-Key header is required.");
        value = value.Trim();
        if (value.Length > 60)
            throw DeliveryOperationException.Validation("Idempotency-Key cannot exceed 60 characters.");
        return value;
    }

    private static string[] NormalizeQrCodes(IReadOnlyCollection<string> values)
    {
        if (values.Count == 0)
            throw DeliveryOperationException.Validation("At least one QR code is required.");
        var result = values.Select(value => string.IsNullOrWhiteSpace(value)
                ? throw DeliveryOperationException.Validation("QR code cannot be empty.")
                : value.Trim().ToUpperInvariant())
            .Order(StringComparer.Ordinal).ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw DeliveryOperationException.Validation("QR codes cannot be repeated.");
        return result;
    }

    private static string HashReceipt(Guid orderId, IEnumerable<string> qrCodes, string? note)
    {
        var canonical = $"{orderId:N}|{string.Join(',', qrCodes)}|{note}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void EnsureVersion(int current, int expected, string entity)
    {
        if (current != expected)
            throw DeliveryOperationException.Conflict(
                $"{entity} version is {current}; refresh and retry.");
    }

    private static void ValidateNote(string? note)
    {
        if (note?.Trim().Length > 500)
            throw DeliveryOperationException.Validation("Note cannot exceed 500 characters.");
    }

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string NormalizeRequired(string value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw DeliveryOperationException.Validation($"{field} is required.");
        value = value.Trim();
        if (value.Length > maxLength)
            throw DeliveryOperationException.Validation($"{field} cannot exceed {maxLength} characters.");
        return value;
    }

    private static void ValidateCapacity(decimal capacity)
    {
        if (capacity <= 0)
            throw DeliveryOperationException.Validation("Vehicle capacity must be greater than zero.");
    }

    private static void TransitionOrder(SalesOrder order, string status, DateTimeOffset now)
    {
        order.Status = status;
        order.Version++;
        order.UpdatedAt = now;
    }

    private static OrderStatusHistory NewHistory(
        Guid orderId, string from, string to, Guid actorUserId, string reason, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            SalesOrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = actorUserId,
            Reason = reason,
            ChangedAt = now
        };

    private static QrLabelEvent NewLabelEvent(
        BatchQrLabel label,
        Guid actorUserId,
        string operationKey,
        string eventType,
        string from,
        string to,
        string reference,
        string note,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            BatchQrLabelId = label.Id,
            WarehouseId = label.CurrentWarehouseId,
            PerformedByUserId = actorUserId,
            OperationKey = operationKey,
            EventType = eventType,
            FromStatus = from,
            ToStatus = to,
            Quantity = label.Quantity,
            ReferenceCode = reference,
            Note = note,
            OccurredAt = now
        };
}
