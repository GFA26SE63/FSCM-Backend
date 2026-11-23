using FSCM.Persistence.WarehouseWork;

namespace FSCM.IntegrationTests.WarehouseWork;

/// <summary>Pure grouping rules of "Hàng chờ nhận từ nhà máy" (no database needed).</summary>
public sealed class FactoryDispatchGroupingTests
{
    private static readonly Guid Warehouse = Guid.NewGuid();
    private static readonly DateTimeOffset Morning = new(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);

    private static FactoryInboundPallet Pallet(string code, Guid? note, string? noteCode = null, DateTimeOffset? at = null, Guid? receipt = null) =>
        new(Guid.NewGuid(), code, Warehouse, at ?? Morning, "BID-1", "SKU", "Sữa", 10, new DateOnly(2026, 12, 31), receipt,
            note, noteCode, note is null ? null : "59C-123.45", note is null ? null : "Tài xế Ba");

    [Fact]
    public void Pallets_of_one_dispatch_note_form_one_row_and_legacy_pallets_stay_alone()
    {
        var note = Guid.NewGuid();
        var openReceipt = Guid.NewGuid();
        var legacyReceipt = Guid.NewGuid();
        var pallets = new[]
        {
            Pallet("PID-2", note, "PXN-F01-20261122-0001"),
            Pallet("PID-1", note, "PXN-F01-20261122-0001"),
            Pallet("PID-OLD", null, at: Morning.AddHours(-2), receipt: legacyReceipt),
        };

        var rows = WarehouseWorkService.GroupFactoryDispatches(pallets,
            key => key == $"factory-dispatch:{note:N}" ? openReceipt : null);

        Assert.Equal(2, rows.Length);
        var legacy = rows[0];
        Assert.Null(legacy.DispatchNoteId);
        Assert.Equal("PID-OLD", legacy.DispatchCode);
        Assert.Equal(legacyReceipt, legacy.ReceiptDocumentId);
        var grouped = rows[1];
        Assert.Equal(note, grouped.DispatchNoteId);
        Assert.Equal("PXN-F01-20261122-0001", grouped.DispatchCode);
        Assert.Equal(["PID-1", "PID-2"], grouped.Pallets.Select(p => p.PalletCode));
        Assert.Equal(("59C-123.45", "Tài xế Ba"), (grouped.VehicleNumber, grouped.DriverName));
        Assert.Equal(openReceipt, grouped.ReceiptDocumentId);
    }

    [Fact]
    public void Receipt_history_groups_older_per_pallet_receipts_of_one_dispatch_note()
    {
        var note = Guid.NewGuid();
        var newNote = Guid.NewGuid();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(), e = Guid.NewGuid(), open = Guid.NewGuid();
        FactoryReceiptSource Doc(Guid id, int minute, Guid? ownNote = null, string? code = null) =>
            new(id, Morning.AddMinutes(minute), Warehouse, ownNote, code);
        string Json(params string[] dispositions) => System.Text.Json.JsonSerializer.Serialize(
            dispositions.Select((x, i) => new FSCM.Domain.Entities.ReceiptCartonInspection($"CID-{i}", x, x == "Accepted" ? null : "Lý do", null)));
        var docs = new[] { Doc(a, 1), Doc(b, 2), Doc(c, 3), Doc(d, 4), Doc(e, 5, newNote, "PXN-NEW"), Doc(open, 6) };
        var pallets = new[]
        {
            // a + b: same note known from Traceability.
            new FactoryReceivedPalletSource(Guid.NewGuid(), "PID-A", a, Morning, Json("Accepted", "Missing"), note, "PXN-OLD"),
            new FactoryReceivedPalletSource(Guid.NewGuid(), "PID-B", b, Morning, Json("Damaged"), note, "PXN-OLD"),
            // c + d: note unknown (Traceability down), but they left together.
            new FactoryReceivedPalletSource(Guid.NewGuid(), "PID-C", c, Morning.AddHours(1), null, null, null),
            new FactoryReceivedPalletSource(Guid.NewGuid(), "PID-D", d, Morning.AddHours(1), null, null, null),
            new FactoryReceivedPalletSource(Guid.NewGuid(), "PID-E1", e, Morning.AddHours(2), null, newNote, "PXN-NEW"),
            new FactoryReceivedPalletSource(Guid.NewGuid(), "PID-E2", e, Morning.AddHours(2), null, newNote, "PXN-NEW"),
        };

        var groups = WarehouseWorkService.GroupFactoryReceipts(docs, pallets);

        Assert.Equal(4, groups.Length);
        var old = groups.Single(g => g.DispatchCode == "PXN-OLD");
        Assert.Equal([a, b], old.DocumentIds);
        Assert.Equal(["PID-A", "PID-B"], old.Pallets.Select(p => p.PalletCode));
        Assert.Equal((1, 0, 1), (old.Pallets[0].Accepted, old.Pallets[0].Damaged, old.Pallets[0].Missing));
        Assert.Equal([c, d], groups.Single(g => g.DocumentIds.Contains(c)).DocumentIds);
        Assert.Equal([e], groups.Single(g => g.DispatchCode == "PXN-NEW").DocumentIds);
        Assert.Equal([open], groups.Single(g => g.DocumentIds.Contains(open)).DocumentIds);
        Assert.Equal(open, groups[0].DocumentIds[0]); // newest first
    }

    [Fact]
    public void A_pallet_is_wholly_missing_only_when_every_carton_is_missing()
    {
        string Json(params string[] dispositions) => System.Text.Json.JsonSerializer.Serialize(
            dispositions.Select((d, i) => new FSCM.Domain.Entities.ReceiptCartonInspection($"CID-{i}", d, d == "Accepted" ? null : "Không về", null)));
        Assert.True(WarehouseWorkService.IsWholePalletMissing(Json("Missing", "Missing")));
        Assert.False(WarehouseWorkService.IsWholePalletMissing(Json("Missing", "Accepted")));
        Assert.False(WarehouseWorkService.IsWholePalletMissing(null));
        Assert.False(WarehouseWorkService.IsWholePalletMissing("[]"));
    }

    [Fact]
    public void Expectation_is_complete_only_when_every_pallet_was_received()
    {
        FactoryExpectedPallet P(string code, bool received) =>
            new(Guid.NewGuid(), code, "BID-1", "SKU", "Sữa", 10, new DateOnly(2026, 12, 31), received);
        Assert.False(new FactoryReceiptExpectation(Guid.NewGuid(), "PXN", null, null, [P("A", true), P("B", false)]).AllReceived);
        Assert.True(new FactoryReceiptExpectation(Guid.NewGuid(), "PXN", null, null, [P("A", true), P("B", true)]).AllReceived);
        Assert.False(new FactoryReceiptExpectation(Guid.NewGuid(), "PXN", null, null, []).AllReceived);
    }
}
