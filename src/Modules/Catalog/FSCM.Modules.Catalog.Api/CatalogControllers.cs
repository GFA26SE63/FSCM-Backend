using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Catalog.Domain.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Modules.Catalog.Api;

[ApiController, Route("api/v1/categories"), Authorize]
public sealed class CategoriesController(CategoryService svc) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost, Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Create(CategoryInput x, CancellationToken ct) => Ok(new { code = await svc.CreateAsync(x, ct) });
    [HttpPut("{code}"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Update(string code, CategoryInput x, CancellationToken ct) { await svc.UpdateAsync(code, x, ct); return NoContent(); }
}

/// <summary>Màn "SKU": Nháp → Đã publish → Inactive; published is read-only, clone to change.</summary>
[ApiController, Route("api/v1/products"), Authorize]
public sealed class ProductsController(ProductService svc) : ControllerBase
{
    [HttpGet] public Task<PagedResult<ProductDto>> Search([FromQuery] int page = 1, [FromQuery] int size = 20, [FromQuery] string? search = null, [FromQuery] string? status = null, [FromQuery] string? category = null, CancellationToken ct = default)
        => svc.SearchAsync(new PageRequest(page, size, search), status, category, ct);
    [HttpGet("{sku}")] public Task<ProductDto> Get(string sku, CancellationToken ct) => svc.GetAsync(sku, ct);
    [HttpPost, Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Create(ProductDetails x, CancellationToken ct) => Ok(new { sku = await svc.CreateDraftAsync(x, ct) });
    [HttpPut("{sku}"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Update(string sku, ProductDetails x, CancellationToken ct) { await svc.UpdateDraftAsync(sku, x, ct); return NoContent(); }
    [HttpPost("{sku}/publish"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Publish(string sku, CancellationToken ct) { await svc.PublishAsync(sku, ct); return NoContent(); }
    [HttpPost("{sku}/inactivate"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Inactivate(string sku, CancellationToken ct) { await svc.InactivateAsync(sku, ct); return NoContent(); }
    [HttpPost("{sku}/clone"), Authorize(Roles = RoleNames.Admin)] public async Task<IActionResult> Clone(string sku, CancellationToken ct) => Ok(new { sku = await svc.CloneAsync(sku, ct) });
}
