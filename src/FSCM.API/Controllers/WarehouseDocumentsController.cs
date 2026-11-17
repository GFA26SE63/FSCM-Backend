using System.Net;
using System.Security.Claims;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Persistence.WarehouseWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Route("api/v1/warehouse-documents")]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.WarehouseKeeper}")]
public sealed class WarehouseDocumentsController(WarehouseWorkService service, IQrCodeRenderer qr, QrLabelService labels) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;
    [HttpGet("issue-workflow")]
    public object IssueWorkflow() => new { Version = 2, IssueBeforeScan = true, StableIssueId = true };
    [HttpGet]
    public Task<WarehouseWorkResult[]> List(CancellationToken ct) => service.ListAsync(Actor, Role, ct);
    [HttpGet("{id:guid}")]
    public Task<WarehouseWorkResult> Get(Guid id, CancellationToken ct) => service.GetAsync(id, Actor, Role, ct);
    [HttpGet("factory-pending")]
    public Task<FactoryInboundPallet[]> FactoryPending(CancellationToken ct) => service.PendingFactoryAsync(Actor, Role, ct);
    [HttpPost("factory-receipts/from-pallet")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> PrepareFactory(PrepareFactoryReceiptRequest request, CancellationToken ct) =>
        await Get(await service.PrepareFactoryAsync(request, Actor, Role, ct), ct);
    [HttpPost("requests")]
    [Authorize(Roles = SystemRoles.Admin)]
    public async Task<WarehouseWorkResult> CreateRequest(CreateWarehouseRequest request,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct) =>
        await Get(await service.CreateRequestAsync(request, key, Actor, Role, ct), ct);
    [HttpPost("{id:guid}/create-document")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> CreateChild(Guid id,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct) =>
        await Get(await service.CreateChildAsync(id, key, Actor, Role, ct), ct);
    [HttpPost("factory-receipts")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> Factory(FactoryReceiptRequest request,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct) =>
        await Get(await service.CreateFactoryAsync(request, key, Actor, Role, ct), ct);
    [HttpGet("{id:guid}/pallet-preview")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public Task<PalletManifest> PreviewPallet(Guid id, string code, CancellationToken ct) => service.PreviewPalletAsync(id, code, Actor, Role, ct);
    [HttpGet("{id:guid}/receipt-reports")]
    public Task<PalletReceiptReport[]> ReceiptReports(Guid id, CancellationToken ct) => service.ReceiptReportsAsync(id, Actor, Role, ct);
    [HttpPost("{id:guid}/pallet-scans")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public Task<ReceiptPalletScan> ScanPallet(Guid id, ReceiptPalletScanRequest request, CancellationToken ct) =>
        service.ScanReceiptPalletAsync(id, request, Actor, Role, Request.Headers.UserAgent.ToString(), ct);
    [HttpGet("{id:guid}/pallet-scans")]
    public Task<ReceiptPalletScan[]> PalletScans(Guid id, CancellationToken ct) => service.ReceiptScansAsync(id, Actor, Role, ct);
    [HttpGet("{id:guid}/receipt-print")]
    public async Task<ContentResult> ReceiptPrint(Guid id, CancellationToken ct)
    {
        var work = await Get(id, ct);
        var info = await service.ReceiptPrintInfoAsync(id, Actor, Role, ct);
        var reports = await service.ReceiptReportsAsync(id, Actor, Role, ct);
        var scans = await service.ReceiptScansAsync(id, Actor, Role, ct);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        static string At(DateTimeOffset? t) => t?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        var d = work.Document;
        var html = new System.Text.StringBuilder("<!doctype html><html lang='vi'><meta charset='utf-8'><title>Phiếu nhập kho</title><style>body{font:14px Arial;padding:24px;color:#111}table{width:100%;border-collapse:collapse;margin:16px 0}th,td{border:1px solid #aaa;padding:8px;text-align:left;overflow-wrap:anywhere}td{max-width:260px}@media print{button{display:none}}</style><button onclick='window.print()'>In / Lưu PDF</button><h1>PHIẾU NHẬP KHO</h1>");
        html.Append($"<p>Số phiếu: <b>{E(d.Code)}</b></p><p>Kho nhận: {E(info.Warehouse)} · Người lập: {E(info.Creator)}</p><p>Ngày lập: {At(d.CreatedAt)} · Ngày hoàn tất: {At(d.PostedAt)} (UTC+7)</p><p>Trạng thái: {E(d.Status == "Open" ? "Đang xử lý — chưa hoàn tất" : d.Status == "Posted" ? "Đã ghi sổ" : d.Status)}</p><p>{E(d.Note)}</p>");
        html.Append("<h2>Hàng nhập thực tế</h2><table><tr><th>SKU / sản phẩm</th><th>Batch / nguồn</th><th>NSX / HSD</th><th>Carton đạt</th></tr>");
        foreach (var l in work.Lines)
            html.Append($"<tr><td>{E(l.Sku)} · {E(l.ProductName)}</td><td>{E(l.BatchCode)} / {E(l.SourceLotCode)}</td><td>{l.ProductionDate:dd/MM/yyyy} / {l.ExpirationDate:dd/MM/yyyy}</td><td>{l.Quantity}</td></tr>");
        html.Append("</table><h2>Biên bản kiểm đếm</h2><table><tr><th>Pallet / CID</th><th>Kết quả</th><th>Lý do</th><th>Vị trí cách ly</th></tr>");
        foreach (var p in reports)
            foreach (var c in p.Cartons)
                html.Append($"<tr><td>{E(p.PalletCode)} / {E(c.CartonCode)}</td><td>{E(c.Disposition == "Accepted" ? "Đạt" : c.Disposition == "Damaged" ? "Hỏng — cách ly" : "Thiếu")}</td><td>{E(c.Reason)}</td><td>{E(c.QuarantineLocation)}</td></tr>");
        html.Append("</table><h2>Lịch sử quét đối chiếu</h2><table><tr><th>Pallet</th><th>Người quét</th><th>Thời gian máy chủ (UTC+7)</th></tr>");
        foreach (var s in scans)
            html.Append($"<tr><td>{E(s.PalletCode)}</td><td>{E(s.ScannedByName)} ({s.ScannedByUserId})</td><td>{At(s.ScannedAt)}</td></tr>");
        return Content(html.Append("</table><p>Người giao hàng: __________________ &nbsp; Người nhận hàng: __________________</p></html>").ToString(), "text/html; charset=utf-8");
    }
    [HttpPost("{id:guid}/receive-pallet")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> ReceivePallet(Guid id, InspectedPalletRequest request, CancellationToken ct) =>
        await Get(await service.ReceiveInspectedPalletAsync(id, request, Actor, Role, ct), ct);
    public sealed record ScanRequest(string CartonId, int ExpectedVersion);
    public sealed record PostRequest(int ExpectedVersion);
    public sealed record RemoveCartonRequest(string CartonCode, string? ReplacementCode, string Reason, int ExpectedVersion);
    [HttpPost("{id:guid}/remove-carton")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> RemoveCarton(Guid id, RemoveCartonRequest request, CancellationToken ct) =>
        await Get(await service.RemoveCartonAsync(id, request.CartonCode, request.ReplacementCode,
            request.Reason, request.ExpectedVersion, Actor, Role, ct), ct);
    [HttpPost("{id:guid}/prepare-cartons")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> PrepareCartons(Guid id, CancellationToken ct) =>
        await Get(await service.PrepareCartonsAsync(id, Actor, Role, ct), ct);
    [HttpGet("{id:guid}/stock-labels")]
    public async Task<ContentResult> StockLabels(Guid id, CancellationToken ct)
    {
        var work = await Get(id, ct);
        var html = new System.Text.StringBuilder("<!doctype html><html lang='vi'><meta charset='utf-8'><title>Nhãn carton hàng tồn</title><style>body{font-family:Arial,sans-serif}article{display:inline-block;width:280px;margin:8px;border:1px solid;padding:12px;break-inside:avoid}svg{width:180px;height:180px}small{display:block;overflow-wrap:anywhere}</style>");
        foreach (var batch in work.Lines.Where(x => x.BatchId.HasValue).GroupBy(x => x.BatchId!.Value))
            foreach (var label in (await labels.ListAsync(Actor, Role, work.Document.WarehouseId, batch.Key, ct))
                .Where(x => x.Status == QrLabelStatuses.Available && x.Quantity == 1).Take((int)batch.Sum(x => x.Quantity)))
                html.Append($"<article>{qr.RenderSvg(label.QrCode)}<small>{WebUtility.HtmlEncode(label.QrCode)}</small><p>SKU {WebUtility.HtmlEncode(label.Sku)} · Batch {WebUtility.HtmlEncode(label.BatchCode)}</p><p>1 carton · HSD {label.ExpirationDate:dd/MM/yyyy}</p></article>");
        return Content(html.Append("</html>").ToString(), "text/html; charset=utf-8");
    }
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = SystemRoles.Admin)]
    public async Task<WarehouseWorkResult> Cancel(Guid id, PostRequest request, CancellationToken ct) =>
        await Get(await service.CancelRequestAsync(id, request.ExpectedVersion, Actor, Role, ct), ct);
    [HttpPost("{id:guid}/scans")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> Scan(Guid id, ScanRequest request, CancellationToken ct) =>
        await Get(await service.ScanAsync(id, request.CartonId, request.ExpectedVersion, Actor, Role, ct), ct);
    [HttpPost("{id:guid}/post")]
    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    public async Task<WarehouseWorkResult> Post(Guid id, PostRequest request, CancellationToken ct) =>
        await Get(await service.PostAsync(id, request.ExpectedVersion, Actor, Role, ct), ct);
    [HttpGet("{id:guid}/labels")]
    public async Task<ContentResult> Labels(Guid id, CancellationToken ct)
    {
        var result = await Get(id, ct);
        var html = new System.Text.StringBuilder("<!doctype html><html lang='vi'><meta charset='utf-8'><title>Carton QR</title><style>body{font:14px Arial,sans-serif}article{display:inline-block;width:280px;padding:16px;margin:8px;border:1px solid;break-inside:avoid}svg{width:180px;height:180px}small{overflow-wrap:anywhere;display:block}@media print{button{display:none}}</style><button onclick='window.print()'>In nhãn carton</button>");
        foreach (var carton in result.Cartons)
        {
            var line = result.Lines.Single(x => x.Id == carton.LineId);
            html.Append($"<article>{qr.RenderSvg(carton.CartonId)}<small>{WebUtility.HtmlEncode(carton.CartonId)}</small><p>SKU: {WebUtility.HtmlEncode(line.Sku)} · 1 carton</p><p>Batch / lot: {WebUtility.HtmlEncode(line.BatchCode ?? line.SourceLotCode)}</p><p>NSX: {line.ProductionDate:dd/MM/yyyy} · HSD: {line.ExpirationDate:dd/MM/yyyy}</p></article>");
        }
        return Content(html.Append("</html>").ToString(), "text/html; charset=utf-8");
    }
}
