using System.Security.Claims;
using FSCM.API.Infrastructure;
using FSCM.Application.Features.WriteOffs;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.WarehouseKeeper}")]
[Route("api/v1/write-offs")]
public sealed class WriteOffsController(WriteOffService service) : ControllerBase
{
    /// <summary>Đính kèm ảnh bằng chứng hàng hư hỏng / giảm tồn cho một phiếu write-off.</summary>
    [HttpPost("{writeOffId:guid}/attachments")]
    [RequestSizeLimit(ImageUploadLimits.MaxRequestBytes)]
    [ProducesResponseType<WriteOffAttachmentResult>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WriteOffAttachmentResult>> AddAttachment(
        Guid writeOffId, IFormFile file, CancellationToken ct)
    {
        await using var upload = await file.ToImageUploadAsync(ct);
        var result = await service.AddAttachmentAsync(writeOffId, ActorId(), upload, ct);
        return CreatedAtAction(nameof(ListAttachments), new { writeOffId }, result);
    }

    [HttpGet("{writeOffId:guid}/attachments")]
    [ProducesResponseType<IReadOnlyCollection<WriteOffAttachmentResult>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<WriteOffAttachmentResult>> ListAttachments(
        Guid writeOffId, CancellationToken ct) =>
        service.ListAttachmentsAsync(writeOffId, ct);

    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpDelete("{writeOffId:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAttachment(
        Guid writeOffId, Guid attachmentId, CancellationToken ct)
    {
        await service.RemoveAttachmentAsync(writeOffId, attachmentId, ct);
        return NoContent();
    }

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw WriteOffOperationException.Validation("Authenticated user identifier is invalid.");
}
