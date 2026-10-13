using FSCM.Modules.Notifications.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Notifications.Api;

[ApiController, Route("api/v1/notifications"), Authorize]
public sealed class NotificationsController(NotificationService svc) : ControllerBase
{
    [HttpGet] public Task<NotificationListDto> List([FromQuery] bool unreadOnly = false, [FromQuery] int take = 50, CancellationToken ct = default) => svc.ListAsync(unreadOnly, take, ct);
    [HttpPost("{id:guid}/read")] public async Task<IActionResult> Read(Guid id, CancellationToken ct) { await svc.MarkReadAsync(id, ct); return NoContent(); }
}
