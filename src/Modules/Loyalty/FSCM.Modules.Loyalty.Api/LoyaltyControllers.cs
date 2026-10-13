using FSCM.Modules.Loyalty.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Loyalty.Api;

[ApiController, Route("api/v1/loyalty"), Authorize]
public sealed class LoyaltyController(ProgramService programs, LoyaltyQueryService query) : ControllerBase
{
    [HttpGet("programs")] public Task<IReadOnlyList<ProgramDto>> Programs(CancellationToken ct) => programs.ListAsync(ct);
    [HttpPost("programs"), Authorize(Roles = "Admin")] public async Task<IActionResult> Create(ProgramInput x, CancellationToken ct) => Ok(new { code = await programs.CreateAsync(x, ct) });
    [HttpPut("programs/{code}"), Authorize(Roles = "Admin")] public async Task<IActionResult> Update(string code, ProgramInput x, CancellationToken ct) { await programs.UpdateAsync(code, x, ct); return NoContent(); }
    /// <summary>Balance (available / pending / held) and point ledger of one retailer. A retailer can only read their own.</summary>
    [HttpGet("retailers/{code}")] public Task<RetailerPointsDto> Retailer(string code, CancellationToken ct) => query.GetAsync(code, ct);
}

[ApiController, Route("api/v1/gifts"), Authorize]
public sealed class GiftsController(GiftService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<GiftDto>> List([FromQuery] bool activeOnly = false, CancellationToken ct = default) => svc.ListAsync(activeOnly, ct);
    [HttpPost, Authorize(Roles = "Admin")] public async Task<IActionResult> Create(GiftInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
    [HttpPut("{code}"), Authorize(Roles = "Admin")] public async Task<IActionResult> Update(string code, GiftInput x, CancellationToken ct) { await svc.UpdateAsync(code, x, ct); return NoContent(); }
}

[ApiController, Route("api/v1/gift-redemptions"), Authorize]
public sealed class GiftRedemptionsController(RedemptionService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<RedemptionDto>> List([FromQuery] string? status, CancellationToken ct) => svc.ListAsync(status, ct);
    [HttpPost, Authorize(Roles = "Retailer,Admin,Operator")] public async Task<IActionResult> Request(RedeemInput x, CancellationToken ct) => Ok(new { code = await svc.RequestAsync(x, ct) });
    [HttpPost("{code}/approve"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Approve(string code, CancellationToken ct) { await svc.ApproveAsync(code, ct); return NoContent(); }
    [HttpPost("{code}/reject"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Reject(string code, RejectRedemptionInput x, CancellationToken ct) { await svc.RejectAsync(code, x.Reason, ct); return NoContent(); }
    [HttpPost("{code}/deliver"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Deliver(string code, CancellationToken ct) { await svc.DeliverAsync(code, ct); return NoContent(); }
    [HttpPost("{code}/complete"), Authorize(Roles = "Admin,Operator")] public async Task<IActionResult> Complete(string code, CancellationToken ct) { await svc.CompleteAsync(code, ct); return NoContent(); }
}
