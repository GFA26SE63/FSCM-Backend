using FSCM.Domain.Entities;

namespace FSCM.IntegrationTests.Persistence;

public sealed partial class FactoryTraceabilityTests
{
    [Fact]
    public void Inspection_preserves_each_disposition_and_normalizes_replay()
    {
        ReceiptCartonInspection[] rows = [new("C", "Missing", " Short ", null), new("A", "Accepted", null, null), new("B", "Damaged", "Broken", " Q-01 ")];
        var json = ReceiptInspection.Normalize(["A", "B", "C"], rows);
        Assert.Equal(json, ReceiptInspection.Normalize(["C", "B", "A"], rows.Reverse().ToArray()));
        var factoryRows = rows.Select(x => new Fscm.Traceability.Models.ReceiptCartonInspection(x.CartonCode, x.Disposition, x.Reason, x.QuarantineLocation)).ToArray();
        Assert.Equal(json, Fscm.Traceability.Models.ReceiptInspection.Normalize(["A", "B", "C"], factoryRows));
        var result = ReceiptInspection.Read(json);
        Assert.Equal(1, result.Count(x => x.Disposition == "Accepted"));
        Assert.Equal("Q-01", result.Single(x => x.Disposition == "Damaged").QuarantineLocation);
        Assert.Equal("Short", result.Single(x => x.Disposition == "Missing").Reason);
    }

    [Fact]
    public void Inspection_rejects_duplicate_missing_and_foreign_cartons()
    {
        Assert.Throws<ArgumentException>(() => ReceiptInspection.Normalize(["A", "B"], [new("A", "Accepted", null, null), new("A", "Accepted", null, null)]));
        Assert.Throws<ArgumentException>(() => ReceiptInspection.Normalize(["A", "B"], [new("A", "Accepted", null, null)]));
        Assert.Throws<ArgumentException>(() => ReceiptInspection.Normalize(["A"], [new("X", "Accepted", null, null)]));
    }

    [Fact]
    public void Inspection_requires_reason_and_quarantine_location()
    {
        Assert.Throws<ArgumentException>(() => ReceiptInspection.Normalize(["A"], [new("A", "Damaged", "Broken", " ")]));
        Assert.Throws<ArgumentException>(() => ReceiptInspection.Normalize(["A"], [new("A", "Missing", " ", null)]));
        Assert.Throws<ArgumentException>(() => ReceiptInspection.Normalize(["A"], [new("A", "Unknown", null, null)]));
    }

    [Fact]
    public void Inspection_all_missing_or_damaged_has_no_sellable_cartons()
    {
        var rows = ReceiptInspection.Read(ReceiptInspection.Normalize(["A", "B"], [new("A", "Missing", "Short", null), new("B", "Damaged", "Wet", "Q-02")]));
        Assert.DoesNotContain(rows, x => x.Disposition == "Accepted");
    }
}
