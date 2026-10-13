using FSCM.Modules.Inventory.Application.Rotation;
using FSCM.Modules.Inventory.Domain.Rotation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Inventory.Api;

/// <summary>Màn "Luân chuyển cận hạn" (Admin): nightly proposals, score detail of every candidate, approve / edit quantity / skip.</summary>
[ApiController, Route("api/v1/rotation"), Authorize(Roles = "Admin")]
public sealed class RotationController(RotationService svc, RotationJob job) : ControllerBase
{
    [HttpGet("proposals")] public Task<IReadOnlyList<ProposalDto>> Proposals([FromQuery] DateOnly? runDate, CancellationToken ct) => svc.ByRunDateAsync(runDate, ct);
    [HttpGet("proposals/{id:guid}/trace")] public async Task<IActionResult> Trace(Guid id, CancellationToken ct) => Content(await svc.TraceAsync(id, ct), "application/json");
    [HttpPost("proposals/{id:guid}/approve")] public async Task<IActionResult> Approve(Guid id, CancellationToken ct) => Ok(new { transferCode = await svc.ApproveAsync(id, ct) });
    [HttpPost("proposals/{id:guid}/skip")] public async Task<IActionResult> Skip(Guid id, CancellationToken ct) { await svc.SkipAsync(id, ct); return NoContent(); }
    public sealed record QtyBody(int Quantity);
    [HttpPut("proposals/{id:guid}")] public async Task<IActionResult> Edit(Guid id, QtyBody b, CancellationToken ct) { await svc.ChangeQuantityAsync(id, b.Quantity, ct); return NoContent(); }
    /// <summary>Run the rotation now (same code as the 02:00 job).</summary>
    [HttpPost("run")] public async Task<IActionResult> Run(CancellationToken ct) { await job.RunAsync(ct); return Accepted(); }
    [HttpGet("settings")] public Task<RotationSettings> GetSettings(CancellationToken ct) => svc.GetSettingsAsync(ct);
    [HttpPut("settings")] public async Task<IActionResult> PutSettings(RotationSettings s, CancellationToken ct) { await svc.SaveSettingsAsync(s, ct); return NoContent(); }
}
