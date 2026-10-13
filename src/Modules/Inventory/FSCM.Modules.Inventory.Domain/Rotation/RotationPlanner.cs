namespace FSCM.Modules.Inventory.Domain.Rotation;

/// <summary>Weights and limits for near-expiry rotation (configured separately from order allocation).</summary>
public sealed record RotationSettings(
    decimal WeightSellThrough = 0.5m, decimal WeightDistance = 0.3m, decimal WeightCoverage = 0.2m,
    decimal DmaxWarehouseKm = 40, int MinTransferQty = 10, int RoundTo = 10, int VelocityDays = 14, int DeliveryDaysToRetailer = 1);

/// <summary>Sales velocity of one retailer (or a group with the same MLOR ratio) for one SKU at one warehouse.</summary>
public sealed record CustomerVelocity(string Label, decimal CasesPerDay, decimal MlorRatio);

public sealed record BatchSnapshot(string Code, string Warehouse, string Sku, DateOnly Expiry, int Available, int ShelfLifeDays, decimal UnitCost);

public sealed record WarehouseInfo(string Code, string Name, bool IsCentral);

/// <summary>Transit between two warehouses taken from the shared transit table (null = not served).</summary>
public sealed record Leg(decimal Km, int TransitDays, decimal TripCost);

public sealed record WindowRow(string Label, decimal Velocity, decimal MlorRatio, decimal MlorDays, decimal Window, decimal Sellable);

public sealed record CandidateResult(string Warehouse, string Name, decimal Km, int? TransitDays, int? RemainingDays, decimal SalesCapacity, int OlderAhead, int ReceivingCapacity, bool Passed, string Reason);

public sealed record CandidateScore(string Warehouse, string Name, int Tentative, decimal SellThroughSafety, decimal Distance, decimal Coverage, decimal Score);

public sealed record Turn(int No, string Warehouse, int ExcessBefore, int Quantity, int ExcessAfter, string Note);

public sealed record Assignment(string ToWarehouse, string ToName, decimal Km, int TransitDays, int Quantity, decimal Score, decimal SalvageValue, decimal TripCost);

public sealed record RotationPlan(BatchSnapshot Batch, int RemainingDays, IReadOnlyList<WindowRow> SourceWindows, int OlderAhead, int Sellable, int Excess,
    IReadOnlyList<CandidateResult> Candidates, IReadOnlyList<CandidateScore> Scores, IReadOnlyList<Turn> Turns, IReadOnlyList<Assignment> Assignments, int Remainder)
{
    public bool NoDestination => Assignments.Count == 0;
}

/// <summary>
/// CR-01 – near-expiry lateral transshipment, weighted like order allocation:
///   Step 1  sellable window W = L − m·SL − delivery days per retailer; C = Σ v·W − older stock; excess E = q − C
///   Step 2  hard filter destinations (regional, ≤ Dmax_kho, v &gt; 0, N ≥ min qty, salvage ≥ trip cost)
///   Step 3  score S = wE·f_E + wD·f_D + wQ·f_Q
///           f_E sell-through safety = 1 − (Q_ahead + tentative) / sales capacity
///           f_D distance = 1 − km / Dmax_kho
///           f_Q excess coverage = tentative / excess
///   Step 4  greedy: best score first, x = floor(min(E, N) / round) · round, recompute scores, repeat
/// Pure function: the job loads snapshots, the planner decides, Admin approves.
/// </summary>
public sealed class RotationPlanner(RotationSettings s)
{
    public IReadOnlyList<RotationPlan> Plan(
        DateOnly today,
        IReadOnlyList<BatchSnapshot> batches,
        IReadOnlyDictionary<string, WarehouseInfo> warehouses,
        Func<string, string, IReadOnlyList<CustomerVelocity>> velocity,   // (warehouse, sku) → customers
        Func<string, string, Leg?> leg)                                    // (from, to) → transit
    {
        var added = new Dictionary<(string Wh, string Sku), int>();
        var plans = new List<RotationPlan>();
        foreach (var b in batches.Where(x => x.Available > 0 && x.Expiry > today && !warehouses[x.Warehouse].IsCentral && velocity(x.Warehouse, x.Sku).Count > 0)
                                 .OrderBy(x => x.Expiry).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            var L = b.Expiry.DayNumber - today.DayNumber;
            var src = Windows(velocity(b.Warehouse, b.Sku), L, b.ShelfLifeDays);
            var olderSrc = OlderAhead(batches, b.Warehouse, b, today);
            var C = (int)Math.Max(0, Math.Floor(src.Sum(r => r.Sellable)) - olderSrc);
            var E = b.Available - C;
            if (E <= 0) continue;

            var cands = new List<(CandidateResult R, decimal Cap, int Ahead, int N, Leg? Leg)>();
            foreach (var w in warehouses.Values.Where(w => w.Code != b.Warehouse).OrderBy(w => w.Code, StringComparer.Ordinal))
            {
                var lg = leg(b.Warehouse, w.Code);
                CandidateResult Fail(string why, decimal cap = 0, int ahead = 0, int n = 0, int? rem = null) =>
                    new(w.Code, w.Name, lg?.Km ?? 0, lg?.TransitDays, rem, cap, ahead, n, false, why);
                if (w.IsCentral) { cands.Add((Fail("Kho tổng không xuất bán cho retailer (v = 0)"), 0, 0, 0, lg)); continue; }
                if (lg is null || lg.Km > s.DmaxWarehouseKm) { cands.Add((Fail($"Ngoài Dmax_kho {s.DmaxWarehouseKm} km"), 0, 0, 0, lg)); continue; }
                var cust = velocity(w.Code, b.Sku);
                if (cust.Count == 0) { cands.Add((Fail($"Kho không bán {b.Sku} (v = 0)"), 0, 0, 0, lg)); continue; }
                var Ld = L - lg.TransitDays;
                var cap = Windows(cust, Ld, b.ShelfLifeDays).Sum(r => r.Sellable);
                var ahead = OlderAhead(batches, w.Code, b, today) + added.GetValueOrDefault((w.Code, b.Sku));
                var N = (int)Math.Max(0, Math.Floor(cap) - ahead);
                if (N < s.MinTransferQty) { cands.Add((Fail($"Receiving capacity N = {N} < {s.MinTransferQty}", cap, ahead, N, Ld), cap, ahead, N, lg)); continue; }
                var x0 = RoundDown(Math.Min(E, N));
                if (x0 * b.UnitCost < lg.TripCost) { cands.Add((Fail("Salvage value < trip cost", cap, ahead, N, Ld), cap, ahead, N, lg)); continue; }
                cands.Add((new CandidateResult(w.Code, w.Name, lg.Km, lg.TransitDays, Ld, cap, ahead, N, true, "Đạt"), cap, ahead, N, lg));
            }

            var pool = cands.Where(c => c.R.Passed).ToList();
            CandidateScore Score((CandidateResult R, decimal Cap, int Ahead, int N, Leg? Leg) c, int left)
            {
                var tent = Math.Min(left, c.N);
                var fE = c.Cap > 0 ? Math.Max(0, 1 - (c.Ahead + tent) / c.Cap) : 0;
                var fD = 1 - c.R.Km / s.DmaxWarehouseKm;
                var fQ = (decimal)tent / left;
                return new CandidateScore(c.R.Warehouse, c.R.Name, tent, R3(fE), R3(fD), R3(fQ), R3(s.WeightSellThrough * fE + s.WeightDistance * fD + s.WeightCoverage * fQ));
            }
            var scores = pool.Select(c => Score(c, E)).OrderByDescending(x => x.Score).ThenBy(x => Km(pool, x.Warehouse)).ToList();

            var turns = new List<Turn>(); var assigns = new List<Assignment>(); var leftover = E;
            while (leftover >= s.MinTransferQty && pool.Count > 0)
            {
                var best = pool.Select(c => (c, sc: Score(c, leftover))).OrderByDescending(t => t.sc.Score).ThenBy(t => t.c.R.Km).First();
                pool.Remove(best.c);
                var x = RoundDown(Math.Min(leftover, best.c.N));
                if (x < s.MinTransferQty || x * b.UnitCost < best.c.Leg!.TripCost)
                { turns.Add(new Turn(turns.Count + 1, best.c.R.Name, leftover, 0, leftover, "Bỏ qua: dưới lượng tối thiểu hoặc không bõ chi phí")); continue; }
                turns.Add(new Turn(turns.Count + 1, best.c.R.Name, leftover, x, leftover - x, $"S = {best.sc.Score:0.000}"));
                assigns.Add(new Assignment(best.c.R.Warehouse, best.c.R.Name, best.c.R.Km, best.c.Leg.TransitDays, x, best.sc.Score, x * b.UnitCost, best.c.Leg.TripCost));
                leftover -= x;
                added[(best.c.R.Warehouse, b.Sku)] = added.GetValueOrDefault((best.c.R.Warehouse, b.Sku)) + x;
            }
            plans.Add(new RotationPlan(b, L, src, olderSrc, C, E, cands.Select(c => c.R).ToList(), scores, turns, assigns, leftover));
        }
        return plans;
    }

    private IReadOnlyList<WindowRow> Windows(IReadOnlyList<CustomerVelocity> cust, int remainingDays, int shelfLife) =>
        cust.Select(c =>
        {
            var mlor = c.MlorRatio * shelfLife;
            var w = Math.Max(0, remainingDays - mlor - s.DeliveryDaysToRetailer);
            return new WindowRow(c.Label, c.CasesPerDay, c.MlorRatio, mlor, w, c.CasesPerDay * w);
        }).ToList();

    /// <summary>Same-SKU stock at the warehouse that expires on or before this batch – FEFO ships it first.</summary>
    private static int OlderAhead(IReadOnlyList<BatchSnapshot> all, string wh, BatchSnapshot b, DateOnly today) =>
        all.Where(x => x.Warehouse == wh && x.Sku == b.Sku && x.Code != b.Code && x.Expiry > today && x.Expiry <= b.Expiry).Sum(x => x.Available);

    private int RoundDown(int q) => q / s.RoundTo * s.RoundTo;
    private static decimal R3(decimal x) => Math.Round(x, 3, MidpointRounding.AwayFromZero);
    private static decimal Km(IEnumerable<(CandidateResult R, decimal, int, int, Leg?)> pool, string wh) => pool.First(p => p.R.Warehouse == wh).R.Km;
}
