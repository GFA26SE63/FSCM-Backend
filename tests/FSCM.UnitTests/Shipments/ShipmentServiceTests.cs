using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Shipments;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.QrLabels;
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

    [Fact]
    public async Task Receipt_records_actuals_and_completes_order_when_all_shipments_are_received()
    {
        var fixture = new Fixture();
        var (shipment, line, batchId, qrCode) = fixture.AddDeliveringShipment(quantity: 8);

        var result = await fixture.Service.ConfirmReceiptAsync(
            shipment.Id,
            new ConfirmShipmentReceiptData([qrCode],
                [new ConfirmShipmentReceiptLineData(line.OrderLineId, batchId, 8, 0)],
                shipment.Version, "Đã nhận đủ"),
            "receipt-full-1", fixture.RetailerUserId, SystemRoles.Retailer, default);

        Assert.Equal(ShipmentStatuses.Received, result.Status);
        Assert.Equal(8, result.ReceivedQuantity);
        Assert.Equal(OrderStatuses.Delivered, fixture.Order.Status);
        Assert.NotNull(result.Receipt);
        Assert.Equal(0, result.Receipt!.Lines.Single().MissingQuantity);
        Assert.Equal(QrLabelStatuses.Delivered, fixture.Store.Labels.Single().Label.Status);

        var retry = await fixture.Service.ConfirmReceiptAsync(
            shipment.Id,
            new ConfirmShipmentReceiptData([qrCode],
                [new ConfirmShipmentReceiptLineData(line.OrderLineId, batchId, 8, 0)],
                shipment.Version, "Đã nhận đủ"),
            "receipt-full-1", fixture.RetailerUserId, SystemRoles.Retailer, default);
        Assert.Equal(result.Receipt.Id, retry.Receipt!.Id);
        Assert.Single(fixture.Store.Receipts);
    }

    [Fact]
    public async Task Receipt_with_missing_or_damaged_quantity_creates_shipment_exception()
    {
        var fixture = new Fixture();
        var (shipment, line, batchId, qrCode) = fixture.AddDeliveringShipment(quantity: 8);

        var result = await fixture.Service.ConfirmReceiptAsync(
            shipment.Id,
            new ConfirmShipmentReceiptData([qrCode],
                [new ConfirmShipmentReceiptLineData(line.OrderLineId, batchId, 6, 2)],
                shipment.Version, "Thiếu hai thùng, hai thùng hư"),
            "receipt-exception-1", fixture.RetailerUserId, SystemRoles.Retailer, default);

        Assert.Equal(ShipmentStatuses.Exception, result.Status);
        Assert.Equal(OrderStatuses.Delivering, fixture.Order.Status);
        var actual = Assert.Single(result.Receipt!.Lines);
        Assert.Equal(6, actual.ReceivedQuantity);
        Assert.Equal(2, actual.DamagedQuantity);
        Assert.Equal(2, actual.MissingQuantity);
    }

    [Fact]
    public async Task Receipt_rejects_actual_quantity_outside_invariant()
    {
        var fixture = new Fixture();
        var (shipment, line, batchId, qrCode) = fixture.AddDeliveringShipment(quantity: 8);

        var exception = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.ConfirmReceiptAsync(
                shipment.Id,
                new ConfirmShipmentReceiptData([qrCode],
                    [new ConfirmShipmentReceiptLineData(line.OrderLineId, batchId, 5, 6)],
                    shipment.Version, null),
                "receipt-invalid-1", fixture.RetailerUserId, SystemRoles.Retailer, default));

        Assert.Equal("validation_error", exception.Code);
        Assert.Empty(fixture.Store.Receipts);
    }

    [Fact]
    public async Task Issuing_a_part_takes_the_goods_and_the_hold_down_together()
    {
        var fixture = new Fixture();
        var (shipment, balance, hold) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);

        await fixture.Service.ConfirmIssueAsync(
            shipment.Id, new ConfirmShipmentIssueData(shipment.Version),
            Guid.NewGuid(), SystemRoles.WarehouseKeeper, CancellationToken.None);

        // Approval raised Reserved and left OnHand alone; issuing is where both fall.
        Assert.Equal(38m, balance.PhysicalQuantity);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(38m, balance.AvailableQuantity);
        Assert.Equal(ReservationStatuses.Consumed, hold.Status);
        Assert.Equal(ShipmentStatuses.Issued, shipment.Status);
    }

    [Fact]
    public async Task Issuing_writes_one_stock_movement_naming_the_shipment()
    {
        var fixture = new Fixture();
        var (shipment, _, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);

        await fixture.Service.ConfirmIssueAsync(
            shipment.Id, new ConfirmShipmentIssueData(shipment.Version),
            Guid.NewGuid(), SystemRoles.WarehouseKeeper, CancellationToken.None);

        var movement = Assert.Single(fixture.Store.InventoryTransactions);
        Assert.Equal(InventoryTransactionTypes.OrderIssue, movement.TransactionType);
        Assert.Equal(-12m, movement.PhysicalQuantityDelta);
        Assert.Equal(-12m, movement.ReservedQuantityDelta);
        Assert.Equal(shipment.ShipmentCode, movement.ReferenceCode);
    }

    [Fact]
    public async Task Confirming_twice_does_not_deduct_twice()
    {
        var fixture = new Fixture();
        var (shipment, balance, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);
        var actor = Guid.NewGuid();

        await fixture.Service.ConfirmIssueAsync(
            shipment.Id, new ConfirmShipmentIssueData(shipment.Version), actor,
            SystemRoles.WarehouseKeeper, CancellationToken.None);
        // A keeper whose connection dropped presses the button again.
        await fixture.Service.ConfirmIssueAsync(
            shipment.Id, new ConfirmShipmentIssueData(shipment.Version), actor,
            SystemRoles.WarehouseKeeper, CancellationToken.None);

        Assert.Equal(38m, balance.PhysicalQuantity);
        Assert.Single(fixture.Store.InventoryTransactions);
    }

    [Fact]
    public async Task A_part_cannot_leave_while_another_warehouse_is_still_picking()
    {
        var fixture = new Fixture();
        var (ready, balance, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);
        fixture.Store.Shipments.Add(new Shipment
        {
            Id = Guid.NewGuid(), SalesOrderId = fixture.Order.Id, WarehouseId = fixture.WarehouseB,
            RunPickingListId = Guid.NewGuid(), ShipmentCode = "SHIP-002099",
            Status = ShipmentStatuses.WaitingToPick, TotalQuantity = 5m, Version = 0
        });

        var error = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.ConfirmIssueAsync(
                ready.Id, new ConfirmShipmentIssueData(ready.Version), Guid.NewGuid(),
                SystemRoles.WarehouseKeeper, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
        // Nothing moved: the whole order waits, not just the slow warehouse.
        Assert.Equal(50m, balance.PhysicalQuantity);
        Assert.Equal(ShipmentStatuses.Ready, ready.Status);
    }

    [Fact]
    public async Task One_warehouse_in_exception_stops_every_part_that_has_not_left()
    {
        var fixture = new Fixture();
        var (ready, _, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);
        fixture.Store.Shipments.Add(new Shipment
        {
            Id = Guid.NewGuid(), SalesOrderId = fixture.Order.Id, WarehouseId = fixture.WarehouseB,
            RunPickingListId = Guid.NewGuid(), ShipmentCode = "SHIP-002098",
            Status = ShipmentStatuses.Exception, ExceptionReason = "Hàng hỏng khi soạn",
            TotalQuantity = 5m, Version = 1
        });

        var error = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.ConfirmIssueAsync(
                ready.Id, new ConfirmShipmentIssueData(ready.Version), Guid.NewGuid(),
                SystemRoles.WarehouseKeeper, CancellationToken.None));

        Assert.Contains("Hàng hỏng khi soạn", error.Message);
    }

    [Fact]
    public async Task A_prepaid_order_that_has_not_been_paid_cannot_leave()
    {
        var fixture = new Fixture();
        fixture.Order.PaymentMethod = OrderPaymentMethods.Prepaid;
        fixture.PaymentStore.Payment!.Status = PaymentStatuses.Unpaid;
        var (ready, balance, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);

        var error = await Assert.ThrowsAsync<ShipmentOperationException>(() =>
            fixture.Service.ConfirmIssueAsync(
                ready.Id, new ConfirmShipmentIssueData(ready.Version), Guid.NewGuid(),
                SystemRoles.WarehouseKeeper, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
        Assert.Equal(50m, balance.PhysicalQuantity);
    }

    [Fact]
    public async Task A_credit_order_does_not_have_to_be_paid_before_it_leaves()
    {
        var fixture = new Fixture();
        // Credit is settled on terms and COD on delivery, so neither gates the door.
        fixture.PaymentStore.Payment!.Status = PaymentStatuses.Unpaid;
        var (ready, balance, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);

        await fixture.Service.ConfirmIssueAsync(
            ready.Id, new ConfirmShipmentIssueData(ready.Version), Guid.NewGuid(),
            SystemRoles.WarehouseKeeper, CancellationToken.None);

        Assert.Equal(38m, balance.PhysicalQuantity);
    }

    [Fact]
    public async Task The_order_is_only_issued_once_every_warehouse_part_has_left()
    {
        var fixture = new Fixture();
        var (first, _, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);
        var (second, _, _) = fixture.AddReadyShipment(fixture.WarehouseB, 5m, onHand: 20m);
        var actor = Guid.NewGuid();

        await fixture.Service.ConfirmIssueAsync(
            first.Id, new ConfirmShipmentIssueData(first.Version), actor,
            SystemRoles.WarehouseKeeper, CancellationToken.None);
        Assert.NotEqual(OrderStatuses.Issued, fixture.Order.Status);

        await fixture.Service.ConfirmIssueAsync(
            second.Id, new ConfirmShipmentIssueData(second.Version), actor,
            SystemRoles.WarehouseKeeper, CancellationToken.None);
        Assert.Equal(OrderStatuses.Issued, fixture.Order.Status);
    }

    [Fact]
    public async Task Readiness_names_the_warehouse_that_is_holding_the_order_up()
    {
        var fixture = new Fixture();
        fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);
        fixture.Store.Shipments.Add(new Shipment
        {
            Id = Guid.NewGuid(), SalesOrderId = fixture.Order.Id, WarehouseId = fixture.WarehouseB,
            RunPickingListId = Guid.NewGuid(), ShipmentCode = "SHIP-002097",
            Status = ShipmentStatuses.WaitingToPick, TotalQuantity = 5m, Version = 0
        });

        var readiness = await fixture.Service.GetOrderReadinessAsync(
            fixture.Order.Id, Guid.NewGuid(), SystemRoles.Operator, CancellationToken.None);

        Assert.False(readiness.IsReady);
        Assert.True(readiness.PaymentSettled);
        Assert.Equal(2, readiness.Parts.Count);
        Assert.Single(readiness.BlockingReasons);
    }

    [Fact]
    public async Task A_part_already_gone_does_not_make_the_order_unready_forever()
    {
        var fixture = new Fixture();
        var (first, _, _) = fixture.AddReadyShipment(fixture.WarehouseA, 12m, onHand: 50m);
        fixture.AddReadyShipment(fixture.WarehouseB, 5m, onHand: 20m);

        await fixture.Service.ConfirmIssueAsync(
            first.Id, new ConfirmShipmentIssueData(first.Version), Guid.NewGuid(),
            SystemRoles.WarehouseKeeper, CancellationToken.None);

        var readiness = await fixture.Service.GetOrderReadinessAsync(
            fixture.Order.Id, Guid.NewGuid(), SystemRoles.Operator, CancellationToken.None);

        Assert.True(readiness.IsReady);
    }

    private sealed class Fixture
    {
        public Guid WarehouseA { get; } = Guid.NewGuid();
        public Guid WarehouseB { get; } = Guid.NewGuid();
        public SalesOrder Order { get; }
        public Guid RetailerUserId { get; } = Guid.NewGuid();
        public FakeStore Store { get; }
        public ShipmentService Service { get; }

        public Driver DriverA { get; }
        public Vehicle VehicleA { get; }

        public Fixture()
        {
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(), OrderCode = "DH-002001", Status = OrderStatuses.Delivering,
                PaymentMethod = OrderPaymentMethods.Credit, CreditTermDays = 7, FinalAmount = 1_000_000
            };
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
            PaymentStore.Payment = new Payment
            {
                Id = Guid.NewGuid(), SalesOrderId = Order.Id, Method = Order.PaymentMethod,
                Status = PaymentStatuses.Unpaid, Amount = Order.FinalAmount,
                AdjustedAmount = Order.FinalAmount, FinalApprovalReference = "PLAN-1"
            };
            Service = new ShipmentService(Store, PaymentStore,
                new PaymentPlanService(PaymentStore), new FixedTimeProvider(Now));
        }

        public FakePaymentStore PaymentStore { get; private set; } = null!;

        public void AddCompletedPickingList(Guid warehouseId, decimal totalPicked) =>
            Store.PendingLists.Add(new PendingShipmentSource(
                Guid.NewGuid(), Order.Id, warehouseId, totalPicked));

        /// <summary>A part that has been picked and whose stock is still held by the approval.</summary>
        public (Shipment Shipment, InventoryBalance Balance, Reservation Hold) AddReadyShipment(
            Guid warehouseId, decimal quantity, decimal onHand = 100m)
        {
            var shipment = new Shipment
            {
                Id = Guid.NewGuid(), SalesOrderId = Order.Id, WarehouseId = warehouseId,
                RunPickingListId = Guid.NewGuid(), ShipmentCode = $"SHIP-00200{Store.Shipments.Count}",
                Status = ShipmentStatuses.Ready, TotalQuantity = quantity, Version = 1
            };
            var batchId = Guid.NewGuid();
            var batch = new Batch
            {
                Id = batchId, WarehouseId = warehouseId, ProductId = Guid.NewGuid(), BatchCode = "LO-900"
            };
            var balance = new InventoryBalance
            {
                Id = Guid.NewGuid(), BatchId = batchId,
                PhysicalQuantity = onHand, ReservedQuantity = quantity,
                AvailableQuantity = onHand - quantity, Version = 1
            };
            var hold = new Reservation
            {
                Id = Guid.NewGuid(), AllocationRunAllocationId = Guid.NewGuid(), BatchId = batchId,
                Quantity = quantity, Status = ReservationStatuses.Active, ReservedAt = Now
            };
            Store.Shipments.Add(shipment);
            Store.Lines[shipment.Id] = [new ShipmentLineView(
                Guid.NewGuid(), 1, batch.ProductId, "SKU001", "Sữa chua", batchId, "LO-900",
                quantity, 10_000, quantity * 10_000)];
            Store.Issuable[shipment.Id] = [new ShipmentIssueRow(batchId, quantity, batch, balance, [hold])];
            return (shipment, balance, hold);
        }

        public (Shipment Shipment, ShipmentLineView Line, Guid BatchId, string QrCode)
            AddDeliveringShipment(decimal quantity)
        {
            var shipment = new Shipment
            {
                Id = Guid.NewGuid(), SalesOrderId = Order.Id, WarehouseId = WarehouseA,
                RunPickingListId = Guid.NewGuid(), ShipmentCode = "SHIP-001000",
                Status = ShipmentStatuses.Delivering, TotalQuantity = quantity, Version = 2
            };
            var batchId = Guid.NewGuid();
            var line = new ShipmentLineView(Guid.NewGuid(), 1, Guid.NewGuid(), "SKU001", "Sữa chua",
                batchId, "LO-001", quantity, 10_000, quantity * 10_000);
            const string qrCode = "TEM-001000";
            Store.Shipments.Add(shipment);
            Store.Lines[shipment.Id] = [line];
            Store.Labels.Add(new ShipmentLabelView(new BatchQrLabel
            {
                Id = Guid.NewGuid(), BatchId = batchId, CurrentWarehouseId = WarehouseA,
                QrCode = qrCode, Quantity = quantity, Status = QrLabelStatuses.InTransit
            }, batchId));
            return (shipment, line, batchId, qrCode);
        }
    }

    private sealed class FakeStore(SalesOrder order) : IShipmentStore
    {
        public List<Shipment> Shipments { get; } = [];
        public List<ReceiptConfirmation> Receipts { get; } = [];
        public List<ReceiptConfirmationItem> ReceiptLines { get; } = [];
        public List<ReceiptConfirmationQrLabel> ReceiptLabels { get; } = [];
        public List<ShipmentLabelView> Labels { get; } = [];
        public Dictionary<Guid, IReadOnlyCollection<ShipmentLineView>> Lines { get; } = [];
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

        public Task<ShipmentView?> FindByReceiptOperationKeyAsync(string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(Receipts.SingleOrDefault(item => item.OperationKey == operationKey) is { ShipmentId: { } shipmentId }
                ? ToView(Shipments.Single(item => item.Id == shipmentId)) : null);

        public Task<SalesOrder?> FindOrderForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(order.Id == orderId ? order : null);

        public Task<IReadOnlyCollection<ShipmentLabelView>> ListLabelsAsync(
            Guid shipmentId, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ShipmentLabelView>>(Labels.ToArray());

        public Task<IReadOnlyCollection<PendingShipmentSource>> ListPendingSourcesAsync(
            Guid? orderId, CancellationToken cancellationToken)
        {
            // A (order, warehouse) already covered by a shipment is no longer pending.
            var pending = PendingLists
                .Where(source => !Shipments.Any(s =>
                                     s.SalesOrderId == source.SalesOrderId && s.WarehouseId == source.WarehouseId) &&
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

        public Dictionary<Guid, IReadOnlyCollection<ShipmentIssueRow>> Issuable { get; } = [];
        public List<InventoryTransaction> InventoryTransactions { get; } = [];

        public Task<IReadOnlyCollection<ShipmentIssueRow>> ListIssuableAsync(
            Guid shipmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Issuable.GetValueOrDefault(shipmentId) ?? []);

        public void AddInventoryTransaction(InventoryTransaction transaction) =>
            InventoryTransactions.Add(transaction);

        public void AddShipment(Shipment shipment) => Shipments.Add(shipment);
        public void AddReceipt(ReceiptConfirmation receipt) => Receipts.Add(receipt);
        public void AddReceiptLine(ReceiptConfirmationItem line) => ReceiptLines.Add(line);
        public void AddReceiptQrLabel(ReceiptConfirmationQrLabel qrLabel) => ReceiptLabels.Add(qrLabel);
        public void AddLabelEvent(QrLabelEvent labelEvent) { }
        public void AddOrderHistory(OrderStatusHistory history) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public void ClearTracking() { }

        private ShipmentView ToView(Shipment shipment)
        {
            var warehouse = new Warehouse { Id = shipment.WarehouseId, Code = "WH", Name = "Kho" };
            var receipt = Receipts.SingleOrDefault(item => item.ShipmentId == shipment.Id);
            var receiptView = receipt is null ? null : new ShipmentReceiptView(
                receipt,
                ReceiptLines.Where(item => item.ReceiptConfirmationId == receipt.Id).ToArray(),
                ReceiptLabels.Where(item => item.ReceiptConfirmationId == receipt.Id)
                    .Select(link => Labels.Single(label => label.Label.Id == link.BatchQrLabelId).Label.QrCode)
                    .ToArray());
            var lines = Lines.GetValueOrDefault(shipment.Id) ?? [];
            if (receiptView is not null)
            {
                var actuals = receiptView.Lines.Where(item => item.SalesOrderLineId.HasValue)
                    .ToDictionary(item => (item.SalesOrderLineId!.Value, item.BatchId));
                lines = lines.Select(line => actuals.TryGetValue((line.OrderLineId, line.BatchId), out var actual)
                    ? line with
                    {
                        ReceivedQuantity = actual.ReceivedQuantity,
                        DamagedQuantity = actual.DamagedQuantity,
                        MissingQuantity = actual.MissingQuantity
                    }
                    : line).ToArray();
            }
            return new ShipmentView(shipment, order, warehouse, "PS-UNKNOWN", lines, receiptView);
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
