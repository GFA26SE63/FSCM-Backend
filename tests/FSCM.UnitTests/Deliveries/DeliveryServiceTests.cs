using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using InventoryAllocation = FSCM.Domain.Entities.Allocation;

namespace FSCM.UnitTests.Deliveries;

public sealed class DeliveryServiceTests
{
    [Fact]
    public async Task Prepaid_order_cannot_dispatch_until_canonical_ledger_is_fully_paid()
    {
        var fixture = new Fixture();
        fixture.Order.PaymentMethod = OrderPaymentMethods.Prepaid;

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() => fixture.DispatchAsync());

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(0, fixture.Inventory.IssueCalls);
    }

    [Fact]
    public async Task Dispatch_snapshots_assignment_and_moves_labels_to_transit()
    {
        var fixture = new Fixture();

        var result = await fixture.DispatchAsync();

        Assert.Equal(DeliveryStatuses.Delivering, result.Status);
        Assert.Equal(OrderStatuses.Delivering, fixture.Order.Status);
        Assert.Equal(fixture.Driver.Code, result.DriverCode);
        Assert.Equal(fixture.Vehicle.PlateNumber, result.PlateNumber);
        Assert.All(fixture.Labels, item => Assert.Equal(QrLabelStatuses.InTransit, item.Label.Status));
        Assert.Equal(1, fixture.Inventory.IssueCalls);
        Assert.Equal(0, fixture.Balance.PhysicalQuantity);
        Assert.Equal(0, fixture.Balance.ReservedQuantity);
        Assert.Contains(fixture.Store.History, item => item.ToStatus == OrderStatuses.Delivering);
    }

    [Fact]
    public async Task Retailer_scans_every_label_and_completes_receipt()
    {
        var fixture = new Fixture();
        await fixture.DispatchAsync();

        var result = await fixture.ReceiveAsync(fixture.Labels.Select(item => item.QrCode).ToArray());

        Assert.Equal(DeliveryStatuses.Delivered, result.Status);
        Assert.Equal(OrderStatuses.Delivered, fixture.Order.Status);
        Assert.NotNull(result.Receipt);
        Assert.Equal(2, result.Receipt.Items.Count);
        Assert.All(fixture.Labels, item => Assert.Equal(QrLabelStatuses.Delivered, item.Label.Status));
    }

    [Fact]
    public async Task Receipt_rejects_missing_label()
    {
        var fixture = new Fixture();
        await fixture.DispatchAsync();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.ReceiveAsync([fixture.Labels[0].QrCode]));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(OrderStatuses.Delivering, fixture.Order.Status);
    }

    [Fact]
    public async Task Receipt_rejects_another_retailer()
    {
        var fixture = new Fixture();
        await fixture.DispatchAsync();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.ConfirmReceiptAsync(
                fixture.Order.Id,
                new(fixture.Labels.Select(item => item.QrCode).ToArray(), fixture.Order.Version, null),
                "receipt-other-retailer",
                Guid.NewGuid(),
                SystemRoles.Retailer,
                default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Dispatch_and_receipt_operation_keys_are_idempotent()
    {
        var fixture = new Fixture();
        var firstDispatch = await fixture.DispatchAsync();
        var repeatedDispatch = await fixture.DispatchAsync(expectedVersion: 3);
        Assert.Equal(firstDispatch.Id, repeatedDispatch.Id);

        var codes = fixture.Labels.Select(item => item.QrCode).ToArray();
        var firstReceipt = await fixture.ReceiveAsync(codes);
        var repeatedReceipt = await fixture.ReceiveAsync(codes, expectedVersion: 4);

        Assert.Equal(firstReceipt.Receipt?.Id, repeatedReceipt.Receipt?.Id);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(4, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task Dispatch_rejects_vehicle_with_insufficient_capacity()
    {
        var fixture = new Fixture();
        fixture.Vehicle.Capacity = 9;

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() => fixture.DispatchAsync());

        Assert.Equal("conflict", exception.Code);
        Assert.Empty(fixture.Store.Deliveries);
    }

    [Fact]
    public async Task Dispatch_rejects_stale_order_version()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.DispatchAsync(expectedVersion: 2));

        Assert.Equal("conflict", exception.Code);
        Assert.Empty(fixture.Store.Deliveries);
    }

    [Fact]
    public async Task Dispatch_rejects_driver_and_vehicle_from_different_warehouses()
    {
        var fixture = new Fixture();
        fixture.Vehicle.WarehouseId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() => fixture.DispatchAsync());

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Receipt_rejects_repeated_qr_code()
    {
        var fixture = new Fixture();
        await fixture.DispatchAsync();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() => fixture.ReceiveAsync(
            [fixture.Labels[0].QrCode, fixture.Labels[0].QrCode]));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Receipt_operation_key_rejects_different_payload()
    {
        var fixture = new Fixture();
        await fixture.DispatchAsync();
        var codes = fixture.Labels.Select(item => item.QrCode).ToArray();
        await fixture.ReceiveAsync(codes);

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.ConfirmReceiptAsync(
                fixture.Order.Id,
                new(codes, fixture.Order.Version, "Nội dung khác"),
                "receipt-test-001",
                fixture.RetailerUserId,
                SystemRoles.Retailer,
                default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Create_driver_auto_generates_code_when_omitted()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateDriverAsync(
            new(fixture.Warehouse.Id, null, "Lê Văn Giao", "0912000022", "GPLX-C-022"),
            SystemRoles.Operator, default);

        Assert.Equal("DRV-10", result.Code);
        Assert.True(result.IsActive);
        Assert.Equal("Lê Văn Giao", result.FullName);
        Assert.Contains(fixture.Store.Drivers, item => item.Id == result.Id);
    }

    [Fact]
    public async Task Create_driver_rejects_duplicate_code()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.CreateDriverAsync(
                new(fixture.Warehouse.Id, fixture.Driver.Code, "Người trùng mã", "0912000033", "GPLX-C-033"),
                SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Create_driver_rejects_unknown_warehouse()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.CreateDriverAsync(
                new(Guid.NewGuid(), null, "Người lạ kho", "0912000044", "GPLX-C-044"),
                SystemRoles.Operator, default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Create_driver_requires_operator_role()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.CreateDriverAsync(
                new(fixture.Warehouse.Id, null, "Không đủ quyền", "0912000055", "GPLX-C-055"),
                SystemRoles.Sales, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Update_driver_changes_contact_fields()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UpdateDriverAsync(
            fixture.Driver.Id, new("Trần Quốc Bảo", "0909999999", "GPLX-C-999"),
            SystemRoles.Operator, default);

        Assert.Equal("0909999999", result.Phone);
        Assert.Equal("GPLX-C-999", result.LicenseNumber);
    }

    [Fact]
    public async Task Set_driver_inactive_toggles_status()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.SetDriverActiveAsync(
            fixture.Driver.Id, false, SystemRoles.Operator, default);

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Create_vehicle_rejects_duplicate_plate()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.CreateVehicleAsync(
                new(fixture.Warehouse.Id, fixture.Vehicle.PlateNumber, "Xe tải 2 tấn", 25),
                SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Create_vehicle_rejects_nonpositive_capacity()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DeliveryOperationException>(() =>
            fixture.Service.CreateVehicleAsync(
                new(fixture.Warehouse.Id, "51D-999.99", "Xe tải 2 tấn", 0),
                SystemRoles.Operator, default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Create_vehicle_persists_and_normalizes_plate()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateVehicleAsync(
            new(fixture.Warehouse.Id, "51d-777.77", "Xe tải 3 tấn", 30),
            SystemRoles.Operator, default);

        Assert.Equal("51D-777.77", result.PlateNumber);
        Assert.Equal(30, result.Capacity);
        Assert.True(result.IsActive);
        Assert.Contains(fixture.Store.Vehicles, item => item.Id == result.Id);
    }

    [Fact]
    public async Task Set_vehicle_inactive_toggles_status()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.SetVehicleActiveAsync(
            fixture.Vehicle.Id, false, SystemRoles.Operator, default);

        Assert.False(result.IsActive);
    }

    private sealed class Fixture
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 15, 4, 0, 0, TimeSpan.Zero);
        public Guid RetailerUserId { get; } = Guid.NewGuid();
        public Warehouse Warehouse { get; } = new() { Id = Guid.NewGuid(), Code = "WH-TD", Name = "Kho Thủ Đức" };
        public Retailer Retailer { get; }
        public SalesOrder Order { get; }
        public Driver Driver { get; }
        public Vehicle Vehicle { get; }
        public InventoryAllocation Allocation { get; }
        public InventoryBalance Balance { get; }
        public List<DeliveryLabelView> Labels { get; }
        public FakeDeliveryStore Store { get; }
        public FakePaymentStore PaymentStore { get; }
        public FakeInventoryLifecycle Inventory { get; }
        public DeliveryService Service { get; }

        public Fixture()
        {
            Retailer = new Retailer
            {
                Id = Guid.NewGuid(),
                UserAccountId = RetailerUserId,
                Code = "10004127",
                Name = "Cửa hàng An Phú"
            };
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = "DH-001040",
                RetailerId = Retailer.Id,
                RetailerCodeSnapshot = Retailer.Code,
                RetailerNameSnapshot = Retailer.Name,
                Status = OrderStatuses.Issued,
                Version = 3
            };
            Driver = new Driver
            {
                Id = Guid.NewGuid(),
                WarehouseId = Warehouse.Id,
                Code = "NG-011",
                FullName = "Trần Quốc Bảo",
                Phone = "0901000011",
                LicenseNumber = "GPLX-C-011",
                IsActive = true
            };
            Vehicle = new Vehicle
            {
                Id = Guid.NewGuid(),
                WarehouseId = Warehouse.Id,
                PlateNumber = "51D-318.26",
                VehicleType = "Xe tải 1.5 tấn",
                Capacity = 20,
                IsActive = true
            };
            var batchId = Guid.NewGuid();
            Allocation = new InventoryAllocation
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                WarehouseId = Warehouse.Id,
                AllocatedQuantity = 10,
                Status = AllocationInventoryStatuses.Reserved
            };
            Balance = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                PhysicalQuantity = 10,
                ReservedQuantity = 10,
                AvailableQuantity = 0,
                Version = 0
            };
            Labels = [CreateLabel("TEM-001010", 5, batchId), CreateLabel("TEM-001011", 5, batchId)];
            Store = new FakeDeliveryStore(this);
            Store.Warehouses.Add(Warehouse.Id);
            PaymentStore = new FakePaymentStore(this);
            Inventory = new FakeInventoryLifecycle(this);
            Service = new DeliveryService(
                Store, new FakePaymentPlan(), PaymentStore, Inventory, new FixedTimeProvider(Now));
        }

        public Task<DeliveryResult> DispatchAsync(int expectedVersion = 3) =>
            Service.DispatchAsync(Order.Id,
                new(Driver.Id, Vehicle.Id, expectedVersion, "Giao ca sáng"),
                "dispatch-test-001", Guid.NewGuid(), SystemRoles.Operator, default);

        public Task<DeliveryResult> ReceiveAsync(IReadOnlyCollection<string> codes, int? expectedVersion = null) =>
            Service.ConfirmReceiptAsync(Order.Id,
                new(codes, expectedVersion ?? Order.Version, "Đã nhận đủ hàng"),
                "receipt-test-001", RetailerUserId, SystemRoles.Retailer, default);

        private DeliveryLabelView CreateLabel(string code, decimal quantity, Guid batchId)
        {
            var label = new BatchQrLabel
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                CurrentWarehouseId = Warehouse.Id,
                QrCode = code,
                Quantity = quantity,
                Status = QrLabelStatuses.Picked,
                Version = 2
            };
            return new(label, label.BatchId, code, "DH-001040A");
        }
    }

    private sealed class FakeDeliveryStore(Fixture fixture) : IDeliveryStore
    {
        public List<Delivery> Deliveries { get; } = [];
        public List<ReceiptConfirmation> Receipts { get; } = [];
        public List<ReceiptConfirmationItem> ReceiptItems { get; } = [];
        public List<QrLabelEvent> Events { get; } = [];
        public List<OrderStatusHistory> History { get; } = [];

        public Task<IReadOnlyCollection<Driver>> ListDriversAsync(bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Driver>>([fixture.Driver]);

        public Task<IReadOnlyCollection<Vehicle>> ListVehiclesAsync(bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Vehicle>>([fixture.Vehicle]);

        public Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Driver?>(id == fixture.Driver.Id ? fixture.Driver : null);

        public Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Vehicle?>(id == fixture.Vehicle.Id ? fixture.Vehicle : null);

        public List<Driver> Drivers { get; } = [];
        public List<Vehicle> Vehicles { get; } = [];
        public HashSet<Guid> Warehouses { get; } = [];
        public int NextDriverSequence { get; set; } = 10;

        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(Warehouses.Contains(warehouseId));

        public Task<bool> DriverCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken) =>
            Task.FromResult(Drivers.Any(item => item.Code == code && (!excludeId.HasValue || item.Id != excludeId.Value)) ||
                            (fixture.Driver.Code == code && (!excludeId.HasValue || fixture.Driver.Id != excludeId.Value)));

        public Task<bool> VehiclePlateExistsAsync(string plateNumber, Guid? excludeId, CancellationToken cancellationToken) =>
            Task.FromResult(Vehicles.Any(item => item.PlateNumber == plateNumber && (!excludeId.HasValue || item.Id != excludeId.Value)) ||
                            (fixture.Vehicle.PlateNumber == plateNumber && (!excludeId.HasValue || fixture.Vehicle.Id != excludeId.Value)));

        public Task<string> NextDriverCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"DRV-{NextDriverSequence++:00}");

        public Task<Driver?> FindDriverForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Drivers.SingleOrDefault(item => item.Id == id) ??
                (id == fixture.Driver.Id ? fixture.Driver : null));

        public Task<Vehicle?> FindVehicleForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Vehicles.SingleOrDefault(item => item.Id == id) ??
                (id == fixture.Vehicle.Id ? fixture.Vehicle : null));

        public void AddDriver(Driver driver) => Drivers.Add(driver);
        public void AddVehicle(Vehicle vehicle) => Vehicles.Add(vehicle);

        public Task<DeliveryOrderView?> FindOrderAsync(Guid orderId, bool forUpdate,
            CancellationToken cancellationToken) => Task.FromResult<DeliveryOrderView?>(
            orderId == fixture.Order.Id ? new(fixture.Order, fixture.Retailer) : null);

        public Task<IReadOnlyCollection<DeliveryLabelView>> ListOrderLabelsAsync(Guid orderId, bool forUpdate,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<DeliveryLabelView>>(fixture.Labels);

        public Task<IReadOnlyCollection<DeliveryInventoryIssue>> ListOrderInventoryIssuesAsync(
            Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<DeliveryInventoryIssue>>([
                new(fixture.Allocation.Id, fixture.Allocation.BatchId, fixture.Balance.Version)
            ]);

        public Task<DeliveryView?> FindByOrderAsync(Guid orderId, bool forUpdate,
            CancellationToken cancellationToken) => Task.FromResult(
            Deliveries.SingleOrDefault(item => item.SalesOrderId == orderId) is { } delivery
                ? ToView(delivery)
                : null);

        public Task<DeliveryView?> FindByOperationKeyAsync(string operationKey,
            CancellationToken cancellationToken) => Task.FromResult(
            Deliveries.SingleOrDefault(item => item.OperationKey == operationKey) is { } delivery
                ? ToView(delivery)
                : null);

        public Task<DeliveryView?> FindByReceiptOperationKeyAsync(string operationKey,
            CancellationToken cancellationToken) => Task.FromResult(
            Receipts.SingleOrDefault(item => item.OperationKey == operationKey) is { } receipt
                ? ToView(Deliveries.Single(item => item.SalesOrderId == receipt.SalesOrderId))
                : null);

        public Task<IReadOnlyCollection<DeliveryView>> ListAsync(Guid actorUserId, string actorRole,
            string? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<DeliveryView>>(Deliveries.Select(ToView).ToArray());

        public Task<bool> CanViewAsync(DeliveryView delivery, Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) => Task.FromResult(
            actorRole != SystemRoles.Retailer || actorUserId == fixture.RetailerUserId);

        public void AddDelivery(Delivery delivery) => Deliveries.Add(delivery);
        public void AddReceipt(ReceiptConfirmation receipt) => Receipts.Add(receipt);
        public void AddReceiptItem(ReceiptConfirmationItem item) => ReceiptItems.Add(item);
        public void AddLabelEvent(QrLabelEvent labelEvent) => Events.Add(labelEvent);
        public void AddOrderHistory(OrderStatusHistory history) => History.Add(history);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }

        private DeliveryView ToView(Delivery delivery)
        {
            var receipt = Receipts.SingleOrDefault(item => item.SalesOrderId == delivery.SalesOrderId);
            var items = receipt is null
                ? []
                : ReceiptItems.Where(item => item.ReceiptConfirmationId == receipt.Id)
                    .Select(item => new ReceiptItemView(item,
                        fixture.Labels.Single(label => label.Label.Id == item.BatchQrLabelId).QrCode)).ToArray();
            return new(delivery, fixture.Order, fixture.Retailer, receipt, items);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePaymentPlan : IPaymentPlanService
    {
        public Task CreateForFinalApprovedOrderAsync(SalesOrder order, Guid approvalActorUserId,
            DateTimeOffset approvedAt, string finalApprovalReference,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CreateForDeliveredOrderAsync(SalesOrder order, Guid receiptActorUserId,
            DateTimeOffset deliveredAt, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePaymentStore(Fixture fixture) : IPaymentStore
    {
        private readonly Payment ledger = new()
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatuses.Unpaid,
            Amount = 100_000,
            ConfirmedAmount = 0,
            FinalApprovedAt = DateTimeOffset.UtcNow,
            FinalApprovalReference = "RUN-TEST-01"
        };

        public Task<PaymentView?> FindByOrderAsync(Guid orderId, bool forUpdate,
            CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(
            orderId == fixture.Order.Id ? new(ledger, fixture.Order, fixture.Retailer) : null);
        public Task<PaymentView?> FindByOperationKeyAsync(string operationKey,
            CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(null);
        public Task<PaymentView?> FindByPayOsOrderCodeAsync(long payOsOrderCode, bool forUpdate,
            CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(null);
        public Task<IReadOnlyCollection<PaymentView>> ListAsync(Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PaymentView>>([]);
        public Task<bool> CanViewAsync(PaymentView view, Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) => Task.FromResult(false);
        public void Add(Payment payment) => throw new NotSupportedException();
        public void AddAuditLog(AuditLog auditLog) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }
    }

    private sealed class FakeInventoryLifecycle(Fixture fixture) : IInventoryLifecycleService
    {
        public int IssueCalls { get; private set; }

        public Task<InventoryLifecycleResult> ReserveAsync(Guid allocationId, Guid actorUserId,
            int expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InventoryLifecycleResult> ReleaseAsync(Guid allocationId, Guid actorUserId,
            int expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InventoryLifecycleResult> IssueAsync(Guid allocationId, Guid actorUserId,
            int expectedVersion, CancellationToken cancellationToken)
        {
            IssueCalls++;
            fixture.Balance.PhysicalQuantity -= fixture.Allocation.AllocatedQuantity;
            fixture.Balance.ReservedQuantity -= fixture.Allocation.AllocatedQuantity;
            fixture.Balance.AvailableQuantity = fixture.Balance.PhysicalQuantity - fixture.Balance.ReservedQuantity;
            fixture.Balance.Version++;
            fixture.Allocation.Status = AllocationInventoryStatuses.Issued;
            return Task.FromResult(new InventoryLifecycleResult(
                fixture.Allocation.Id, fixture.Allocation.BatchId, fixture.Warehouse.Id,
                fixture.Allocation.AllocatedQuantity, fixture.Allocation.Status,
                fixture.Balance.PhysicalQuantity, fixture.Balance.ReservedQuantity,
                fixture.Balance.AvailableQuantity, fixture.Balance.Version));
        }
    }
}
