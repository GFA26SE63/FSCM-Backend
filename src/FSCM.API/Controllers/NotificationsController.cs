using System.Security.Claims;
using FSCM.Application.Features.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationPage>(StatusCodes.Status200OK)]
    public Task<NotificationPage> List(
        [FromQuery] bool? isRead,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(UserId(), new(isRead, type, page, pageSize), cancellationToken);

    [HttpGet("badges")]
    [ProducesResponseType<NotificationBadgeResult>(StatusCodes.Status200OK)]
    public Task<NotificationBadgeResult> Badges(CancellationToken cancellationToken) =>
        service.GetBadgesAsync(UserId(), cancellationToken);

    [HttpPatch("{notificationId:guid}/read")]
    [ProducesResponseType<NotificationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<NotificationResult> MarkRead(
        Guid notificationId, CancellationToken cancellationToken) =>
        service.MarkReadAsync(UserId(), notificationId, cancellationToken);

    [HttpPatch("read-all")]
    [ProducesResponseType<MarkAllNotificationsReadResult>(StatusCodes.Status200OK)]
    public Task<MarkAllNotificationsReadResult> MarkAllRead(CancellationToken cancellationToken) =>
        service.MarkAllReadAsync(UserId(), cancellationToken);

    private Guid UserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw NotificationOperationException.Validation("Authenticated user identifier is invalid.");
}
