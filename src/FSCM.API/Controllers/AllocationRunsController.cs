using System.Security.Claims;
using FSCM.API.Contracts.AllocationRuns;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/allocation-runs")]
public sealed class AllocationRunsController(
    AllocationRunService runs,
    AllocationPlanApprovalService approvals) : ControllerBase
{
    [HttpGet("{runId:guid}")]
    public Task<AllocationRunResult> Get(Guid runId, CancellationToken cancellationToken) =>
        runs.GetAsync(runId, cancellationToken);

    [HttpGet("by-date/{businessDate}")]
    public Task<AllocationRunResult> ByDate(DateOnly businessDate, CancellationToken cancellationToken) =>
        runs.GetByBusinessDateAsync(businessDate, cancellationToken);

    [HttpGet("{runId:guid}/plan-versions")]
    public Task<IReadOnlyCollection<AllocationPlanVersionResult>> PlanVersions(
        Guid runId, CancellationToken cancellationToken) =>
        approvals.ListAsync(runId, cancellationToken);

    [HttpGet("plan-versions/{versionId:guid}")]
    public Task<AllocationPlanVersionResult> PlanVersion(Guid versionId, CancellationToken cancellationToken) =>
        approvals.GetAsync(versionId, cancellationToken);

    /// <summary>Approves the allocation and its prices as one decision.</summary>
    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("plan-versions/{versionId:guid}/approve")]
    public Task<AllocationPlanVersionResult> Approve(
        Guid versionId, ApprovePlanVersionRequest request, CancellationToken cancellationToken) =>
        approvals.ApproveAsync(versionId, request.ExpectedVersion, ActorId(), cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("plan-versions/{versionId:guid}/request-recalculation")]
    public Task<AllocationPlanVersionResult> RequestRecalculation(
        Guid versionId, RequestRecalculationRequest request, CancellationToken cancellationToken) =>
        approvals.RequestRecalculationAsync(
            versionId, request.ExpectedVersion, request.Reason, ActorId(), cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw AllocationRunOperationException.Validation("Authenticated user identifier is invalid.");
}
