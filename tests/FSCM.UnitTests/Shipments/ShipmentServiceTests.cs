using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Shipments;

public sealed class ShipmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_reconciles_one_shipment_per_completed_picking_list()
    {
        var fixture = new Fixture();
        // Order fans out across two warehouses → two completed picking lists.
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        fixture.AddCompletedPickingList(fixture.WarehouseB, totalPicked: 5);

        var result = await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal(ShipmentStatuses.Ready, item.Status));
        Assert.Contains(result, item => item.WarehouseId == fixture.WarehouseA && item.TotalQuantity == 8);
        Assert.Contains(result, item => item.WarehouseId == fixture.WarehouseB && item.TotalQuantity == 5);
    }

    [Fact]
    public async Task Reconcile_is_idempotent_across_repeated_reads()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);

        await fixture.Service.ListAsync(Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default);
        await fixture.Service.ListAsync(Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default);

        Assert.Single(fixture.Store.Shipments);
    }

    [Fact]
    public async Task Sync_requires_operator_role()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.SyncOrderAsync(fixture.Order.Id, SystemRoles.Sales, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task List_rejects_invalid_status_filter()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.ListAsync(Guid.Empty, SystemRoles.Operator, null, null, "Nonsense", default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Flag_exception_sets_status_and_reason()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        var created = (await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default)).Single();

        var result = await fixture.Service.FlagExceptionAsync(
            created.Id, new FlagShipmentExceptionData("Thiếu 2 thùng", created.Version),
            SystemRoles.Operator, default);

        Assert.Equal(ShipmentStatuses.Exception, result.Status);
        Assert.Equal("Thiếu 2 thùng", result.ExceptionReason);
    }

    [Fact]
    public async Task Flag_exception_rejects_stale_version()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        var created = (await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default)).Single();

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.FlagExceptionAsync(
                created.Id, new FlagShipmentExceptionData("Lý do", created.Version + 3),
                SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Get_forbidden_when_role_cannot_view_order()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        var created = (await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default)).Single();
        fixture.Store.ViewableOrders.Clear();

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.GetAsync(created.Id, Guid.NewGuid(), SystemRoles.Retailer, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Assign_stores_driver_and_vehicle_snapshots_without_changing_status()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        var created = (await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default)).Single();

        var result = await fixture.Service.AssignAsync(
            created.Id, new AssignShipmentData(fixture.DriverA.Id, fixture.VehicleA.Id, created.Version),
            SystemRoles.Operator, default);

        Assert.Equal(fixture.DriverA.Id, result.DriverId);
        Assert.Equal("DRV-10", result.DriverCode);
        Assert.Equal("51D-100.10", result.PlateNumber);
        Assert.NotNull(result.AssignedAt);
        // F58: assignment does not move the shipment into Delivering.
        Assert.Equal(ShipmentStatuses.Ready, result.Status);
    }

    [Fact]
    public async Task Assign_rejects_driver_from_another_warehouse()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        var created = (await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default)).Single();
        var foreignDriver = new Driver
        {
            Id = Guid.NewGuid(), WarehouseId = fixture.WarehouseB, Code = "DRV-99",
            FullName = "Lạc kho", Phone = "0900000000", LicenseNumber = "X", IsActive = true
        };
        fixture.Store.Drivers.Add(foreignDriver);

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.AssignAsync(
                created.Id, new AssignShipmentData(foreignDriver.Id, fixture.VehicleA.Id, created.Version),
                SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Assign_requires_operator_role()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        var created = (await fixture.Service.ListAsync(
            Guid.Empty, SystemRoles.Operator, fixture.Order.Id, null, null, default)).Single();

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.AssignAsync(
                created.Id, new AssignShipmentData(fixture.DriverA.Id, fixture.VehicleA.Id, created.Version),
                SystemRoles.WarehouseKeeper, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Retailer_order_delivery_view_returns_shipments_and_payment_status()
    {
        var fixture = new Fixture();
        fixture.AddCompletedPickingList(fixture.WarehouseA, totalPicked: 8);
        fixture.AddCompletedPickingList(fixture.WarehouseB, totalPicked: 5);
        fixture.PaymentStore.Payment = new Payment
        {
            Id = Guid.NewGuid(),
            SalesOrderId = fixture.Order.Id,
            Method = "Credit",
            Status = "Unpaid",
            Amount = 1_000_000,
            AdjustedAmount = 1_000_000,
            ConfirmedAmount = 200_000,
            DueDate = new DateOnly(2026, 10, 10) // before "Now" (2026-10-20) → overdue
        };

        var result = await fixture.Service.GetRetailerOrderDeliveryAsync(
            fixture.Order.Id, Guid.NewGuid(), SystemRoles.Retailer, default);

        Assert.Equal(fixture.Order.OrderCode, result.OrderCode);
        Assert.Equal(2, result.Shipments.Count);
        Assert.NotNull(result.Payment);
        Assert.Equal("Overdue", result.Payment!.Status);
        Assert.Equal(800_000, result.Payment.OutstandingAmount);
        Assert.True(result.Payment.DaysOverdue > 0);
    }

    [Fact]
    public async Task Retailer_order_delivery_view_forbidden_when_not_viewable()
    {
        var fixture = new Fixture();
        fixture.Store.ViewableOrders.Clear();

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.GetRetailerOrderDeliveryAsync(
                fixture.Order.Id, Guid.NewGuid(), SystemRoles.Retailer, default));

        Assert.Equal("forbidden", exception.Code);
    }

    private sealed class Fixture
    {
        public Guid WarehouseA { get; } = Guid.NewGuid();
        public Guid WarehouseB { get; } = Guid.NewGuid();
        public SalesOrder Order { get; }
        public FakeStore Store { get; }
        public ShipmentService Service { get; }

        public Driver DriverA { get; }
        public Vehicle VehicleA { get; }

        public Fixture()
        {
            Order = new SalesOrder { Id = Guid.NewGuid(), OrderCode = "DH-002001" };
            Store = new FakeStore(Order);
            Store.ViewableOrders.Add(Order.Id);
            DriverA = new Driver
            {
                Id = Guid.NewGuid(),
                WarehouseId = WarehouseA,
                Code = "DRV-10",
                FullName = "Phạm Văn Chở",
                Phone = "0901222333",
                LicenseNumber = "GPLX-C-010",
                IsActive = true
            };
            VehicleA = new Vehicle
            {
                Id = Guid.NewGuid(),
                WarehouseId = WarehouseA,
                PlateNumber = "51D-100.10",
                VehicleType = "Xe tải 1 tấn",
                Capacity = 15,
                IsActive = true
            };
            Store.Drivers.Add(DriverA);
            Store.Vehicles.Add(VehicleA);
            PaymentStore = new FakePaymentStore(Order);
            Service = new ShipmentService(Store, PaymentStore, new FixedTimeProvider(Now));
        }

        public FakePaymentStore PaymentStore { get; private set; } = null!;

        public void AddCompletedPickingList(Guid warehouseId, decimal totalPicked) =>
            Store.PendingLists.Add(new PendingShipmentSource(
                new PickingList
                {
                    Id = Guid.NewGuid(),
                    SalesOrderId = Order.Id,
                    WarehouseId = warehouseId,
                    PickingCode = $"PK-{warehouseId:N}"[..10]
                },
                Order.Id, warehouseId, totalPicked));
    }

    private sealed class FakeStore(SalesOrder order) : IShipmentStore
    {
        public List<Shipment> Shipments { get; } = [];
        public List<PendingShipmentSource> PendingLists { get; } = [];
        public HashSet<Guid> ViewableOrders { get; } = [];
        private int _nextCode = 1000;

        public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> CanViewOrderAsync(Guid orderId, Guid userId, string role, CancellationToken cancellationToken) =>
            Task.FromResult(role is SystemRoles.Admin or SystemRoles.Operator || ViewableOrders.Contains(orderId));

        public Task<IReadOnlyCollection<ShipmentView>> ListAsync(
            Guid userId, string role, Guid? orderId, Guid? warehouseId, string? status, CancellationToken cancellationToken)
        {
            var query = Shipments.AsEnumerable();
            if (orderId.HasValue) query = query.Where(item => item.SalesOrderId == orderId.Value);
            if (warehouseId.HasValue) query = query.Where(item => item.WarehouseId == warehouseId.Value);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
            return Task.FromResult<IReadOnlyCollection<ShipmentView>>(query.Select(ToView).ToArray());
        }

        public Task<ShipmentView?> FindByIdAsync(Guid id, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(Shipments.SingleOrDefault(item => item.Id == id) is { } shipment ? ToView(shipment) : null);

        public Task<IReadOnlyCollection<PendingShipmentSource>> ListPendingSourcesAsync(
            Guid? orderId, CancellationToken cancellationToken)
        {
            var created = Shipments.Select(item => item.PickingListId).ToHashSet();
            var pending = PendingLists
                .Where(source => !created.Contains(source.PickingList.Id) &&
                                 (!orderId.HasValue || source.SalesOrderId == orderId.Value))
                .ToArray();
            return Task.FromResult<IReadOnlyCollection<PendingShipmentSource>>(pending);
        }

        public Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"SHIP-{_nextCode++:000000}");

        public Task<IReadOnlyCollection<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Shipment>>(
                Shipments.Where(item => item.SalesOrderId == orderId).ToArray());

        public List<Driver> Drivers { get; } = [];
        public List<Vehicle> Vehicles { get; } = [];

        public Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Drivers.SingleOrDefault(item => item.Id == id));

        public Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Vehicles.SingleOrDefault(item => item.Id == id));

        public void AddShipment(Shipment shipment) => Shipments.Add(shipment);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ShipmentView ToView(Shipment shipment)
        {
            var warehouse = new Warehouse { Id = shipment.WarehouseId, Code = "WH", Name = "Kho" };
            var pickingList = PendingLists.SingleOrDefault(source => source.PickingList.Id == shipment.PickingListId)?.PickingList
                ?? new PickingList { Id = shipment.PickingListId, PickingCode = "PK-UNKNOWN" };
            return new ShipmentView(shipment, order, warehouse, pickingList, []);
        }
    }

    private sealed class FakePaymentStore(SalesOrder order) : FSCM.Application.Features.Payments.IPaymentStore
    {
        public FSCM.Domain.Entities.Payment? Payment { get; set; }
        private readonly Retailer _retailer = new() { Id = Guid.NewGuid(), UserAccountId = Guid.NewGuid() };

        public Task<FSCM.Application.Features.Payments.PaymentView?> FindByOrderAsync(
            Guid orderId, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<FSCM.Application.Features.Payments.PaymentView?>(
                Payment is not null && orderId == order.Id
                    ? new(Payment, order, _retailer)
                    : null);

        public Task<FSCM.Application.Features.Payments.PaymentView?> FindByOperationKeyAsync(
            string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult<FSCM.Application.Features.Payments.PaymentView?>(null);
        public Task<FSCM.Application.Features.Payments.PaymentView?> FindByPayOsOrderCodeAsync(
            long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<FSCM.Application.Features.Payments.PaymentView?>(null);
        public Task<IReadOnlyCollection<FSCM.Application.Features.Payments.PaymentView>> ListAsync(
            Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FSCM.Application.Features.Payments.PaymentView>>([]);
        public Task<bool> CanViewAsync(
            FSCM.Application.Features.Payments.PaymentView view, Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public void Add(FSCM.Domain.Entities.Payment payment) { }
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public void ClearTracking() { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
