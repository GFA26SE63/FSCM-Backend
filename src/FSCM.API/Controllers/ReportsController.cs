using System.Security.Claims;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator},{SystemRoles.SalesManager},{SystemRoles.Sales}")]
[Route("api/v1/reports")]
public sealed class ReportsController(ReportService service) : ControllerBase
{
    /// <summary>F76: Sales ranking for a period by recognized revenue after promotions and adjustments.</summary>
    [HttpGet("sales-ranking")]
    public Task<SalesRankingResult> SalesRanking(
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        [FromQuery] Guid? salesGroupId,
        CancellationToken cancellationToken) =>
        service.GetSalesRankingAsync(
            new SalesRankingQuery(fromDate, toDate, salesGroupId),
            GetActorUserId(), GetActorRole(), cancellationToken);

    private Guid GetActorUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw ReportOperationException.Validation("Authenticated user identifier is invalid.");

    private string GetActorRole() =>
        User.FindFirstValue(ClaimTypes.Role)
        ?? throw ReportOperationException.Validation("Authenticated user role is invalid.");
}
