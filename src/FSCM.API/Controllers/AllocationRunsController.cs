using System.Security.Claims;
using FSCM.API.Contracts.AllocationRuns;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;
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
    RunPickingIssueService picking,
    AllocationPriceLookupService prices,
    AllocationTraceService traces,
    OrderService orders,
    AllocationRunResultService results) : ControllerBase
{
    /// <summary>
    /// Runs today's allocation now, for the case where the scheduled job did not. Idempotent on the
    /// business date, and the cutoff comes from the published schedule unless an Admin overrides it
    /// with a later moment on the same day.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = SystemRoles.Admin)]
    public Task<AllocationRunResult> RunNow(
        StartAllocationRunRequest? request, CancellationToken cancellationToken) =>
        runs.RunNowAsync(request?.CutoffAt, cancellationToken);

    [HttpGet("{runId:guid}")]
    public Task<AllocationRunResult> Get(Guid runId, CancellationToken cancellationToken) =>
        runs.GetAsync(runId, cancellationToken);

    [HttpGet("by-date/{businessDate}")]
    public Task<AllocationRunResult> ByDate(DateOnly businessDate, CancellationToken cancellationToken) =>
        runs.GetByBusinessDateAsync(businessDate, cancellationToken);

    /// <summary>F95: closes an approved run and freezes its auditable run report.</summary>
    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{runId:guid}/close")]
    public Task<AllocationRunResult> Close(
        Guid runId, CloseAllocationRunRequest request, CancellationToken cancellationToken) =>
        runs.CloseAsync(runId, request.ExpectedVersion, ActorId(), request.Reason, cancellationToken);

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

    /// <summary>
    /// The price a run settled on for one order, component by component. Trace, collection,
    /// complaints and loyalty all read this rather than recomputing.
    /// </summary>
    [HttpGet("orders/{salesOrderId:guid}/price")]
    public Task<OrderPriceResult> OrderPrice(Guid salesOrderId, CancellationToken cancellationToken) =>
        prices.GetByOrderAsync(salesOrderId, cancellationToken);

    [HttpGet("order-lines/{salesOrderLineId:guid}/price")]
    public Task<OrderPriceLineResult> OrderLinePrice(
        Guid salesOrderLineId, CancellationToken cancellationToken) =>
        prices.GetByOrderLineAsync(salesOrderLineId, cancellationToken);

    /// <summary>
    /// Why the run chose the batches it did for one order: the MLOR it had to clear, every batch
    /// each filter looked at, and every ranking. The price those batches ended up at is a separate
    /// record, read through the price endpoint above.
    /// </summary>
    [HttpGet("orders/{salesOrderId:guid}/trace")]
    public async Task<OrderTraceDetailResult> OrderTrace(Guid salesOrderId, CancellationToken cancellationToken)
    {
        await orders.GetAsync(salesOrderId, ActorId(), ActorRole(), cancellationToken);
        return await traces.GetByOrderAsync(salesOrderId, cancellationToken);
    }

    /// <summary>
    /// F90: what the run decided about one order, once the plan was approved. A short order holds
    /// nothing and cannot be resent keeping its place in the queue; both are fields on the result
    /// so a screen does not have to infer them.
    /// </summary>
    [HttpGet("orders/{salesOrderId:guid}/result")]
    public Task<OrderRunResult> OrderResult(Guid salesOrderId, CancellationToken cancellationToken) =>
        results.GetByOrderAsync(salesOrderId, cancellationToken);

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw AllocationRunOperationException.Validation("Authenticated user identifier is invalid.");

    private string ActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw AllocationRunOperationException.Validation("Authenticated user role is invalid.");
}
