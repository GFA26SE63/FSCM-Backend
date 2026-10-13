using FSCM.Modules.Pricing.Application;
using FSCM.Modules.Pricing.Contracts;
using FSCM.Modules.Pricing.Domain.BatchDiscounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Pricing.Api;

[ApiController, Route("api/v1/promotions"), Authorize]
public sealed class PromotionsController(PromotionService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<PromotionDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpGet("{code}")] public Task<PromotionDto> Get(string code, CancellationToken ct) => svc.GetAsync(code, ct);
    [HttpPost, Authorize(Roles = "Admin")] public async Task<IActionResult> Create(PromotionInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
    [HttpPut("{code}"), Authorize(Roles = "Admin")] public async Task<IActionResult> Update(string code, PromotionInput x, CancellationToken ct) { await svc.UpdateAsync(code, x, ct); return NoContent(); }
    [HttpPost("{code}/pause"), Authorize(Roles = "Admin")] public async Task<IActionResult> Pause(string code, CancellationToken ct) { await svc.PauseAsync(code, ct); return NoContent(); }
    [HttpPost("{code}/resume"), Authorize(Roles = "Admin")] public async Task<IActionResult> Resume(string code, CancellationToken ct) { await svc.ResumeAsync(code, ct); return NoContent(); }
}

[ApiController, Route("api/v1/batch-discounts"), Authorize(Roles = "Admin,Operator")]
public sealed class BatchDiscountsController(BatchDiscountService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<BatchDiscountDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    /// <summary>"Quét tồn kho" – suggestions only, nothing is saved.</summary>
    [HttpGet("suggestions")] public Task<IReadOnlyList<DiscountSuggestion>> Scan(CancellationToken ct) => svc.ScanAsync(ct);
    [HttpPost, Authorize(Roles = "Admin")] public async Task<IActionResult> Apply(IReadOnlyList<ApplyDiscountInput> items, CancellationToken ct) => Ok(new { codes = await svc.ApplyAsync(items, ct) });
    [HttpPost("{code}/end"), Authorize(Roles = "Admin")] public async Task<IActionResult> End(string code, CancellationToken ct) { await svc.EndAsync(code, ct); return NoContent(); }
}

public sealed record QuoteRequest(string RetailerCode, IReadOnlyList<QuoteLineInput> Lines, DateOnly? OnDate);

[ApiController, Route("api/v1/pricing"), Authorize]
public sealed class PricingController(IPricingModule pricing, FSCM.BuildingBlocks.Application.IClock clock) : ControllerBase
{
    [HttpPost("quote")] public Task<QuoteDto> Quote(QuoteRequest r, CancellationToken ct) => pricing.QuoteAsync(r.RetailerCode, r.Lines, r.OnDate ?? clock.Today, ct);
}
