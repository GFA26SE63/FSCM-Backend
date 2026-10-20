using System.Security.Claims;
using FSCM.API.Contracts.QrLabels;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/qr-labels")]
public sealed class QrLabelsController(QrLabelService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<QrLabelResult>> List(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? batchId,
        CancellationToken cancellationToken) =>
        service.ListAsync(GetActorUserId(), GetActorRole(), warehouseId, batchId, cancellationToken);

    [HttpGet("{qrCode}")]
    public Task<QrLabelDetailResult> Get(string qrCode, CancellationToken cancellationToken) =>
        service.GetAsync(qrCode, GetActorUserId(), GetActorRole(), cancellationToken);

    [HttpGet("{qrCode}/print")]
    public async Task<IActionResult> Print(string qrCode, CancellationToken cancellationToken)
    {
        var document = await service.RenderPrintableAsync(qrCode, GetActorUserId(), GetActorRole(), cancellationToken);
        return PrintFile(document);
    }

    [HttpGet("batches/{batchId:guid}/print")]
    public async Task<IActionResult> PrintBatch(Guid batchId, CancellationToken cancellationToken)
    {
        var document = await service.RenderBatchPrintableAsync(batchId, GetActorUserId(), GetActorRole(), cancellationToken);
        return PrintFile(document);
    }

    private FileContentResult PrintFile(QrLabelPrintDocument document) =>
        File(System.Text.Encoding.UTF8.GetBytes(document.Html), document.ContentType, document.FileName);

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost]
    public async Task<IActionResult> Generate(
        GenerateQrLabelRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken)
    {
        var result = await service.GenerateAsync(new(request.BatchId, request.Quantity, request.BlobUrl),
            operationKey, GetActorUserId(), GetActorRole(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { qrCode = result.QrCode }, result);
    }

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost("{qrCode}/split")]
    public async Task<IActionResult> Split(
        string qrCode, SplitQrLabelRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken)
    {
        var result = await service.SplitAsync(qrCode, new(request.Quantity, request.ExpectedVersion),
            operationKey, GetActorUserId(), GetActorRole(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { qrCode = result.QrCode }, result);
    }

    [Authorize(Roles = SystemRoles.WarehouseKeeper)]
    [HttpPost("{qrCode}/scans")]
    public Task<QrLabelResult> Scan(
        string qrCode, ScanQrLabelRequest request,
        [FromHeader(Name = "Idempotency-Key")] string operationKey,
        CancellationToken cancellationToken) =>
        service.ScanAsync(qrCode, new(request.Action, request.WarehouseId, request.DestinationBatchId,
            request.ReferenceCode, request.Note, request.ExpectedVersion), operationKey,
            GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId : throw QrLabelOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw QrLabelOperationException.Validation("Authenticated user role is invalid.");
}
