using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Partners.Application;
using FSCM.Modules.Partners.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Partners.Api;

[ApiController, Route("api/v1/areas"), Authorize(Roles = "Admin,SalesManager,Operator")]
public sealed class AreasController(AreaService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<AreaDto>> List(CancellationToken ct) => svc.AreasAsync(ct);
    [HttpPost, Authorize(Roles = "Admin,SalesManager")] public async Task<IActionResult> Create(AreaDto x, CancellationToken ct) => Ok(new { code = await svc.CreateAreaAsync(x, ct) });
    [HttpPut("{code}"), Authorize(Roles = "Admin,SalesManager")] public async Task<IActionResult> Update(string code, AreaDto x, CancellationToken ct) { await svc.UpdateAreaAsync(code, x, ct); return NoContent(); }
}

[ApiController, Route("api/v1/sales-groups"), Authorize(Roles = "Admin,SalesManager")]
public sealed class SalesGroupsController(AreaService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<SalesGroupDto>> List([FromQuery] string? area, CancellationToken ct) => svc.GroupsAsync(area, ct);
    [HttpPost] public async Task<IActionResult> Create(CreateGroupInput x, CancellationToken ct) => Ok(new { code = await svc.CreateGroupAsync(x, ct) });
    [HttpPut("{code}/members")] public async Task<IActionResult> Members(string code, IReadOnlyList<SalesMemberDto> members, CancellationToken ct) { await svc.SetMembersAsync(code, members, ct); return NoContent(); }
}

[ApiController, Route("api/v1/retailers"), Authorize(Roles = "Admin,Operator,SalesManager,Sales")]
public sealed class RetailersController(RetailerService svc) : ControllerBase
{
    [HttpGet] public Task<PagedResult<RetailerDto>> Search([FromQuery] int page = 1, [FromQuery] int size = 20, [FromQuery] string? search = null, [FromQuery] string? area = null, CancellationToken ct = default)
        => svc.SearchAsync(new PageRequest(page, size, search), area, ct);
    [HttpGet("{code}")] public Task<RetailerDetailDto> Get(string code, CancellationToken ct) => svc.GetAsync(code, ct);
    [HttpPost, Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Create(RetailerInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
    [HttpPut("{code}"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Update(string code, RetailerInput x, CancellationToken ct) { await svc.UpdateAsync(code, x, ct); return NoContent(); }
    [HttpGet("{code}/mlor")] public Task<IReadOnlyList<MlorDto>> Mlor(string code, CancellationToken ct) => svc.MlorAsync(code, ct);
    [HttpPut("{code}/mlor"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> SetMlor(string code, IReadOnlyList<MlorDto> rows, CancellationToken ct) { await svc.SetMlorAsync(code, rows, ct); return NoContent(); }
}

[ApiController, Route("api/v1/retailer-registrations"), Authorize(Roles = "Admin,Sales")]
public sealed class RetailerRegistrationsController(RegistrationService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<RegistrationDto>> List([FromQuery] string? status, CancellationToken ct) => svc.ListAsync(status, ct);
    [HttpPost, Authorize(Roles = RoleNames.Sales)] public async Task<IActionResult> Declare(RegistrationInput x, CancellationToken ct) => Ok(new { code = await svc.DeclareAsync(x, ct) });
    [HttpPost("{code}/approve"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Approve(string code, ApproveRegistrationInput x, CancellationToken ct) => Ok(new { retailerCode = await svc.ApproveAsync(code, x, ct) });
    [HttpPost("{code}/reject"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Reject(string code, RejectInput x, CancellationToken ct) { await svc.RejectAsync(code, x.Reason, ct); return NoContent(); }
}
