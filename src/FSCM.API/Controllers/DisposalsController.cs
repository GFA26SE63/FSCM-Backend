using System.Security.Claims;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Route("api/v1/disposals")]
[Authorize]
public sealed class DisposalsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    private DisposalService Service => new(db, clock);
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public Task<DisposalResult[]> List(CancellationToken ct) => Service.ListAsync(Actor, Role, null, ct);

    [HttpGet("candidates")]
    public Task<DisposalCandidate[]> Candidates(CancellationToken ct) => Service.CandidatesAsync(Actor, Role, ct);

    [HttpGet("{code}")]
    public async Task<DisposalResult> Get(string code, CancellationToken ct) =>
        (await Service.ListAsync(Actor, Role, code, ct)).SingleOrDefault()
        ?? throw InventoryOperationException.NotFound("Không tìm thấy phiếu xử lý tại kho được phép.");

    [HttpPost]
    public async Task<IActionResult> Create(DisposalInput input, CancellationToken ct)
    {
        var items = await Service.CreateAsync(input, Actor, Role, ct);
        if (items.Length == 0) return Ok(new { items });
        return CreatedAtAction(nameof(Get), new { code = items[0].Code }, new { items });
    }
}
