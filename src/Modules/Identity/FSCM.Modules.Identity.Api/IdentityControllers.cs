using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Identity.Application;
using FSCM.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Identity.Api;

[ApiController, Route("api/v1/me"), Authorize]
public sealed class MeController(UserService users, ICurrentUser current) : ControllerBase
{
    [HttpGet] public Task<UserDto> Get(CancellationToken ct) => users.GetAsync(current.UserId, ct);
}

[ApiController, Route("api/v1/users"), Authorize(Roles = "Admin")]
public sealed class UsersController(UserService svc) : ControllerBase
{
    [HttpGet] public Task<PagedResult<UserDto>> Search([FromQuery] int page = 1, [FromQuery] int size = 20, [FromQuery] string? search = null, [FromQuery] string? role = null, CancellationToken ct = default)
        => svc.SearchAsync(new PageRequest(page, size, search), role, ct);
    [HttpGet("{id}")] public Task<UserDto> Get(string id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost] public async Task<IActionResult> Create(CreateUserInput x, CancellationToken ct) => Ok(new { id = await svc.CreateAsync(x, ct) });
    [HttpPut("{id}")] public async Task<IActionResult> Update(string id, UpdateUserInput x, CancellationToken ct) { await svc.UpdateAsync(id, x, ct); return NoContent(); }
}

[ApiController, Route("api/v1/devices"), Authorize]
public sealed class DevicesController(UserService svc, ICurrentUser current) : ControllerBase
{
    /// <summary>Mobile app registers its push token after login.</summary>
    [HttpPost] public async Task<IActionResult> Register(RegisterDeviceInput x, CancellationToken ct) { await svc.RegisterDeviceAsync(current.UserId, x, ct); return NoContent(); }
}
