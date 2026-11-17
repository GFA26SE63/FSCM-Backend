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
