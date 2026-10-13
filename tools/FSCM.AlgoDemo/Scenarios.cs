using FSCM.Modules.Inventory.Domain.Rotation;
using FSCM.Modules.Logistics.Domain.Transit;
using FSCM.Modules.Ordering.Domain.Allocation;

namespace FSCM.AlgoDemo;

/// <summary>
/// The two worked examples of the documents (prototype data, 27/09/2026), shared by the demo console and the unit tests.
/// Transit days and trip costs come from the shared transit table (CR-02), exactly as in production.
/// </summary>
public static class Scenarios
{
    public static readonly DateOnly Today = new(2026, 9, 27);
    public static readonly TransitTable Transit = new(TransitTable.Defaults(new DateOnly(2026, 10, 9)), new DistanceLimits());

    /// <summary>
    /// DH-001030, retailer 10004127 (Tạp hóa Minh Châu, P. Thới An), SKU001 need 60, SL 45 days, MLOR ratio 0.3 → 13.5 days, dispatch 28/09.
    /// Expected: 40 from WH-TA + 20 from WH-TCH (WH-GV fails MLOR, WH-CC is beyond 20 km, WH-TA-25092026 expired).
    /// </summary>
    public static LineAllocationResult AllocationDh001030()
    {
        BatchCandidate C(string code, string wh, string name, int d, int m, int avail, decimal km)
        {
            var q = Transit.Quote(km, RoutePurpose.WarehouseToRetailer);
            return new BatchCandidate(code, wh, name, new DateOnly(2026, m, d), avail, km, q?.TransitDays, q?.TripCost ?? 0);
        }
        var candidates = new[]
        {
            C("WH-TA-SKU001-25092026", "WH-TA", "Kho Thới An", 25, 9, 10, 3.2m),
            C("WH-TA-SKU001-18102026", "WH-TA", "Kho Thới An", 18, 10, 40, 3.2m),
            C("WH-TCH-SKU001-21102026", "WH-TCH", "Kho Tân Chánh Hiệp", 21, 10, 35, 6.5m),
            C("WH-TCH-SKU001-09112026", "WH-TCH", "Kho Tân Chánh Hiệp", 9, 11, 0, 6.5m),
            C("WH-GV-SKU001-06102026", "WH-GV", "Kho Gò Vấp", 6, 10, 50, 9.8m),
            C("WH-TD-SKU001-18102026", "WH-TD", "Kho Tân Định", 18, 10, 80, 17.4m),
            C("WH-CC-SKU001-12102026", "WH-CC", "Kho tổng Củ Chi", 12, 10, 90, 28.6m),
        };
        var engine = new AllocationEngine(new AllocationWeights());
        return engine.Allocate(new AllocationRequest("SKU001", 60, 45, 0.3m, Today, new DateOnly(2026, 9, 28), null), candidates);
    }

    /// <summary>
    /// CR-01 example: batch WH-GV-SKU002-12102026 (80 cases, SL 30, unit cost 180,000đ).
    /// Expected: excess 35, Thới An S = 0.667 > Kho E 0.475 > Kho B 0.273, Kho C (45 km) and Kho D (v = 0) rejected → 30 cases GV → TA, remainder 5.
    /// </summary>
    public static RotationPlan RotationGoVap()
    {
        var wh = new Dictionary<string, WarehouseInfo>
        {
            ["WH-GV"] = new("WH-GV", "Kho Gò Vấp", false),
            ["WH-TA"] = new("WH-TA", "Kho Thới An", false),
            ["WH-E"] = new("WH-E", "Kho E (giả định)", false),
            ["WH-B"] = new("WH-B", "Kho B (giả định)", false),
            ["WH-C"] = new("WH-C", "Kho C (giả định)", false),
            ["WH-D"] = new("WH-D", "Kho D (giả định)", false),
        };
        var batches = new[]
        {
            new BatchSnapshot("WH-GV-SKU002-12102026", "WH-GV", "SKU002", new DateOnly(2026, 10, 12), 80, 30, 180_000),
            new BatchSnapshot("WH-E-SKU002-10102026", "WH-E", "SKU002", new DateOnly(2026, 10, 10), 4, 30, 180_000), // older stock ahead at Kho E
        };
        var velocity = new Dictionary<string, CustomerVelocity[]>
        {
            ["WH-GV"] = new[] { new CustomerVelocity("Nhóm 1", 4, 0.25m), new CustomerVelocity("Nhóm 2", 2, 0.15m) },
            ["WH-TA"] = new[] { new CustomerVelocity("Khách Thới An", 12, 0.25m) },
            ["WH-E"] = new[] { new CustomerVelocity("Khách kho E", 10, 0.30m) },
            ["WH-B"] = new[] { new CustomerVelocity("Khách kho B", 8, 0.25m) },
            ["WH-C"] = new[] { new CustomerVelocity("Khách kho C", 15, 0.25m) },
            ["WH-D"] = Array.Empty<CustomerVelocity>(),
        };
        var km = new Dictionary<string, decimal> { ["WH-TA"] = 9, ["WH-E"] = 5, ["WH-B"] = 25, ["WH-C"] = 45, ["WH-D"] = 7, ["WH-GV"] = 9 };
        Leg? LegOf(string from, string to)
        {
            var d = km[to == "WH-GV" ? from : to];
            var q = Transit.Quote(d, RoutePurpose.WarehouseToWarehouse);
            return q is null ? new Leg(d, 0, 0) : new Leg(d, q.TransitDays, q.TripCost);
        }
        var plans = new RotationPlanner(new RotationSettings()).Plan(Today, batches, wh, (w, _) => velocity[w], LegOf);
        return plans.Single(p => p.Batch.Code == "WH-GV-SKU002-12102026");
    }
}
