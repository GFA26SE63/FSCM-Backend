namespace FSCM.Modules.Ordering.Domain.Allocation;

/// <summary>Weights of the B3 score and the retailer distance limit. Defaults 0.5 / 0.3 / 0.2 and 20 km (SPEC §5).</summary>
public sealed record AllocationWeights(decimal WE = 0.5m, decimal WD = 0.3m, decimal WQ = 0.2m, decimal DmaxRetailerKm = 20m);

/// <param name="TransitDays">Transit days of the shared transit table for this distance; null = distance not served.</param>
public sealed record BatchCandidate(string BatchCode, string Warehouse, string WarehouseName, DateOnly Expiry, int Available, decimal DistanceKm, int? TransitDays, decimal TripCost);

/// <param name="MlorRatio">m, so MLOR = m × SL (retailer-specific or product default).</param>
/// <param name="PromisedDate">Latest acceptable ETA; null = no promised date.</param>
public sealed record AllocationRequest(string Sku, int Need, int ShelfLifeDays, decimal MlorRatio, DateOnly Today, DateOnly DispatchDate, DateOnly? PromisedDate);

// ---- trace tables shown in Portal "Giải thích phân bổ" ----
public sealed record B1Row(string BatchCode, string Warehouse, DateOnly Expiry, int Available, bool Pass, string Result);
public sealed record B2DistanceRow(string BatchCode, string Warehouse, decimal DistanceKm, bool Pass, string Result);
public sealed record B2EtaRow(string BatchCode, string Warehouse, int TransitDays, DateOnly Eta, DateOnly Expiry, int RemainingDays, decimal Mlor, bool Pass, string Result);
public sealed record B3Row(int Rank, string BatchCode, string Warehouse, int RemainingDays, decimal FE, decimal FD, decimal FQ, decimal Score);
public sealed record B4Row(int Rank, string BatchCode, string Warehouse, int Available, int NeedBefore, decimal FQ, decimal Score, int Take, int Left);

public sealed record AllocationTake(string BatchCode, string Warehouse, string WarehouseName, DateOnly Expiry, int Quantity,
    decimal DistanceKm, int TransitDays, DateOnly Eta, int RemainingDays, decimal Score, decimal TripCost);

public sealed record LineAllocationResult(string Sku, int Need, decimal Mlor, int ShelfLifeDays, AllocationWeights Weights,
    IReadOnlyList<B1Row> B1, IReadOnlyList<B2DistanceRow> B2Distance, IReadOnlyList<B2EtaRow> B2Eta,
    IReadOnlyList<B3Row> B3, IReadOnlyList<B4Row> B4, IReadOnlyList<AllocationTake> Takes)
{
    public int Allocated => Takes.Sum(t => t.Quantity);
    public int Short => Need - Allocated;
    /// <summary>B5 – no partial delivery: a line that cannot be covered completely rejects the whole order.</summary>
    public bool Enough => Short <= 0;
}

/// <summary>
/// Weighted allocation (SPEC §5), one order line at a time. Pure function – no I/O – so it is covered by unit tests.
///  B1 hard filter: not expired, available &gt; 0
///  B2 hard filter: distance ≤ Dmax_retailer, ETA ≤ promised date, EXP − ETA ≥ MLOR (= m × SL)
///  B3 score  S = wE·f_E + wD·f_D + wQ·f_Q   (f_E = 1 − (L − MLOR)/(SL − MLOR), f_D = 1 − d/Dmax, f_Q = min(avail, need)/need)
///  B4 greedy: best S first, take min(avail, need), recompute f_Q for the rest, ties by batch code
/// </summary>
public sealed class AllocationEngine(AllocationWeights w)
{
    public AllocationWeights Weights => w;

    public LineAllocationResult Allocate(AllocationRequest r, IReadOnlyList<BatchCandidate> candidates)
    {
        var mlor = r.MlorRatio * r.ShelfLifeDays;
        var ordered = candidates.OrderBy(c => c.Expiry).ThenBy(c => c.BatchCode, StringComparer.Ordinal).ToList();

        // B1
        var b1 = new List<B1Row>(); var p1 = new List<BatchCandidate>();
        foreach (var c in ordered)
        {
            var expired = c.Expiry <= r.Today;
            var pass = !expired && c.Available > 0;
            b1.Add(new B1Row(c.BatchCode, c.Warehouse, c.Expiry, c.Available, pass, expired ? "Loại: đã hết hạn" : c.Available <= 0 ? "Loại: không còn khả dụng" : "Giữ"));
            if (pass) p1.Add(c);
        }
        // B2 – distance
        var b2d = new List<B2DistanceRow>(); var p2 = new List<BatchCandidate>();
        foreach (var c in p1)
        {
            var pass = c.TransitDays is not null && c.DistanceKm <= w.DmaxRetailerKm;
            b2d.Add(new B2DistanceRow(c.BatchCode, c.Warehouse, c.DistanceKm, pass, pass ? "Trong phạm vi" : $"Loại: ngoài {w.DmaxRetailerKm:0.##} km"));
            if (pass) p2.Add(c);
        }
        // B2 – ETA and MLOR
        var b2e = new List<B2EtaRow>(); var eligible = new List<(BatchCandidate C, DateOnly Eta, int L)>();
        foreach (var c in p2)
        {
            var eta = r.DispatchDate.AddDays(c.TransitDays!.Value);
            var L = c.Expiry.DayNumber - eta.DayNumber;
            var late = r.PromisedDate is { } promised && eta > promised;
            var pass = !late && L >= mlor;
            b2e.Add(new B2EtaRow(c.BatchCode, c.Warehouse, c.TransitDays.Value, eta, c.Expiry, L, mlor, pass, late ? "Loại: ETA sau ngày hẹn giao" : L < mlor ? "Loại: không đạt MLOR" : "Đạt"));
            if (pass) eligible.Add((c, eta, L));
        }

        // B3 – score with the full need
        decimal FE(int L) => r.ShelfLifeDays - mlor <= 0 ? 1m : Math.Clamp(1m - (L - mlor) / (r.ShelfLifeDays - mlor), 0m, 1m);
        decimal FD(decimal km) => Math.Clamp(1m - km / w.DmaxRetailerKm, 0m, 1m);
        decimal FQ(int avail, int need) => need <= 0 ? 0m : Math.Min(avail, need) / (decimal)need;
        decimal Score(decimal fe, decimal fd, decimal fq) => w.WE * fe + w.WD * fd + w.WQ * fq;
        static decimal R3(decimal x) => Math.Round(x, 3, MidpointRounding.AwayFromZero);

        var b3 = eligible
            .Select(e => (e.C, e.L, fe: FE(e.L), fd: FD(e.C.DistanceKm), fq: FQ(e.C.Available, r.Need)))
            .Select(x => (x.C, x.L, x.fe, x.fd, x.fq, s: Score(x.fe, x.fd, x.fq)))
            .OrderByDescending(x => x.s).ThenBy(x => x.C.BatchCode, StringComparer.Ordinal)
            .Select((x, i) => new B3Row(i + 1, x.C.BatchCode, x.C.Warehouse, x.L, R3(x.fe), R3(x.fd), R3(x.fq), R3(x.s))).ToList();

        // B4 – greedy; f_Q is recomputed against what is still missing
        var pool = eligible.ToList(); var left = r.Need; var turn = 0;
        var b4 = new List<B4Row>(); var takes = new List<AllocationTake>();
        while (left > 0 && pool.Count > 0)
        {
            turn++;
            var best = pool
                .Select(e => (e, fe: FE(e.L), fd: FD(e.C.DistanceKm), fq: FQ(e.C.Available, left)))
                .Select(x => (x.e, x.fe, x.fd, x.fq, s: Score(x.fe, x.fd, x.fq)))
                .OrderByDescending(x => x.s).ThenBy(x => x.e.C.BatchCode, StringComparer.Ordinal).First();
            pool.Remove(best.e);
            var take = Math.Min(left, best.e.C.Available); var before = left; left -= take;
            b4.Add(new B4Row(turn, best.e.C.BatchCode, best.e.C.Warehouse, best.e.C.Available, before, R3(best.fq), R3(best.s), take, left));
            takes.Add(new AllocationTake(best.e.C.BatchCode, best.e.C.Warehouse, best.e.C.WarehouseName, best.e.C.Expiry, take,
                best.e.C.DistanceKm, best.e.C.TransitDays!.Value, best.e.Eta, best.e.L, R3(best.s), best.e.C.TripCost));
        }
        foreach (var rest in pool) b4.Add(new B4Row(0, rest.C.BatchCode, rest.C.Warehouse, rest.C.Available, 0, 0, 0, 0, 0)); // not needed
        return new LineAllocationResult(r.Sku, r.Need, mlor, r.ShelfLifeDays, w, b1, b2d, b2e, b3, b4, takes);
    }

    /// <summary>Estimated delivery cost: one trip per warehouse that ships part of the order (displayed only, never a scoring weight).</summary>
    public static IReadOnlyList<(string Warehouse, string Name, decimal Km, decimal Cost)> DeliveryCost(IEnumerable<AllocationTake> takes) =>
        takes.GroupBy(t => t.Warehouse).OrderBy(g => g.Key, StringComparer.Ordinal)
             .Select(g => (g.Key, g.First().WarehouseName, g.First().DistanceKm, g.First().TripCost)).ToList();
}
