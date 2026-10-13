using FSCM.AlgoDemo;

// Prints the B1–B4 trace of the allocation example and the 4-step trace of the rotation example,
// then checks the expected results of the documents. Exit code 1 if anything differs.
var failures = new List<string>();
void Expect(bool ok, string what) { if (!ok) failures.Add(what); }

Console.OutputEncoding = System.Text.Encoding.UTF8;
var a = Scenarios.AllocationDh001030();
Console.WriteLine("== Phân bổ DH-001030 · SKU001 cần 60 · MLOR = 0,3 × 45 = 13,5 ngày ==");
Console.WriteLine("B1 (hết hạn / hết hàng):");
foreach (var r in a.B1) Console.WriteLine($"  {r.BatchCode,-24} {r.Available,4}  {r.Result}");
Console.WriteLine("B2a (≤ 20 km):");
foreach (var r in a.B2Distance) Console.WriteLine($"  {r.BatchCode,-24} {r.DistanceKm,5} km  {r.Result}");
Console.WriteLine("B2b–d (ETA, MLOR):");
foreach (var r in a.B2Eta) Console.WriteLine($"  {r.BatchCode,-24} ETA {r.Eta:dd/MM}  còn {r.RemainingDays} ngày  {r.Result}");
Console.WriteLine("B3 (S = 0,5·f_E + 0,3·f_D + 0,2·f_Q):");
foreach (var r in a.B3) Console.WriteLine($"  #{r.Rank} {r.BatchCode,-24} f_E {r.FE:0.000}  f_D {r.FD:0.000}  f_Q {r.FQ:0.000}  S {r.Score:0.000}");
Console.WriteLine("B4 (greedy):");
foreach (var r in a.B4) Console.WriteLine($"  #{r.Rank} {r.BatchCode,-24} cần {r.NeedBefore,3}  f_Q {r.FQ:0.000}  S {r.Score:0.000}  lấy {r.Take,3}  còn {r.Left}");
Console.WriteLine("Kết quả: " + string.Join(" + ", a.Takes.Select(t => $"{t.Quantity} {t.Warehouse}")));
Expect(a.Enough, "allocation covers 60");
Expect(a.Takes.Count == 2 && a.Takes[0] is { Warehouse: "WH-TA", Quantity: 40 } && a.Takes[1] is { Warehouse: "WH-TCH", Quantity: 20 }, "allocation = 40 WH-TA + 20 WH-TCH");

var p = Scenarios.RotationGoVap();
Console.WriteLine();
Console.WriteLine($"== Luân chuyển {p.Batch.Code} · {p.Batch.Available} thùng · L = {p.RemainingDays} ngày ==");
foreach (var w in p.SourceWindows) Console.WriteLine($"  {w.Label}: v {w.Velocity} · MLOR {w.MlorDays} ngày · W {w.Window} ngày · bán được {w.Sellable}");
Console.WriteLine($"  Sellable C = {p.Sellable} · Excess E = {p.Excess}");
Console.WriteLine("Hard filter:");
foreach (var c in p.Candidates) Console.WriteLine($"  {c.Name,-18} {c.Km,4} km  N = {c.ReceivingCapacity,3}  {c.Reason}");
Console.WriteLine("Scoring:");
foreach (var s in p.Scores) Console.WriteLine($"  {s.Name,-18} tạm {s.Tentative,3}  f_E {s.SellThroughSafety:0.000}  f_D {s.Distance:0.000}  f_Q {s.Coverage:0.000}  S {s.Score:0.000}");
Console.WriteLine("Greedy:");
foreach (var t in p.Turns) Console.WriteLine($"  lượt {t.No}: {t.Warehouse} · dư {t.ExcessBefore} → chuyển {t.Quantity} → còn {t.ExcessAfter} ({t.Note})");
Console.WriteLine($"Kết quả: {string.Join(", ", p.Assignments.Select(x => $"{x.Quantity} thùng → {x.ToName}"))}; còn lại {p.Remainder} thùng → gợi ý giảm giá");
Expect(p.Sellable == 45 && p.Excess == 35, "rotation C = 45, E = 35");
Expect(p.Scores.Select(s => (s.Warehouse, s.Score)).SequenceEqual(new[] { ("WH-TA", 0.667m), ("WH-E", 0.475m), ("WH-B", 0.273m) }), "rotation scores TA 0.667 > E 0.475 > B 0.273");
Expect(p.Candidates.Count(c => !c.Passed) == 2, "Kho C and Kho D rejected");
Expect(p.Assignments.Count == 1 && p.Assignments[0] is { ToWarehouse: "WH-TA", Quantity: 30 } && p.Remainder == 5, "30 cases GV → TA, remainder 5");

Console.WriteLine();
if (failures.Count == 0) { Console.WriteLine("OK – khớp ví dụ trong tài liệu."); return 0; }
foreach (var f in failures) Console.WriteLine("FAIL: " + f);
return 1;
