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
    AllocationPlanApprovalService approvals,
    RunPickingIssueService picking) : ControllerBase
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

    /// <summary>Issues one picking sheet per warehouse for an approved run. Safe to call twice.</summary>
    [Authorize(Roles = $"{SystemRoles.Admin},{SystemRoles.Operator}")]
    [HttpPost("{runId:guid}/picking-lists")]
    public Task<IReadOnlyCollection<RunPickingListResult>> IssuePickingLists(
        Guid runId, CancellationToken cancellationToken) =>
        picking.IssueAsync(runId, cancellationToken);

    [HttpGet("{runId:guid}/picking-lists")]
    public Task<IReadOnlyCollection<RunPickingListResult>> PickingLists(
        Guid runId, CancellationToken cancellationToken) =>
        picking.ListForRunAsync(runId, cancellationToken);

    [HttpGet("picking-lists/{pickingListId:guid}")]
    public Task<RunPickingListResult> PickingList(Guid pickingListId, CancellationToken cancellationToken) =>
        picking.GetAsync(pickingListId, cancellationToken);

    [HttpGet("warehouses/{warehouseId:guid}/picking-lists")]
    public Task<IReadOnlyCollection<RunPickingListResult>> WarehousePickingLists(
        Guid warehouseId, CancellationToken cancellationToken) =>
        picking.ListForWarehouseAsync(warehouseId, cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw AllocationRunOperationException.Validation("Authenticated user identifier is invalid.");
}
