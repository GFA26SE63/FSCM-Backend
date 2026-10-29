using FSCM.API.Contracts.Catalog;
using FSCM.Application.Features.Catalog;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/categories")]
public sealed class CategoriesController(CatalogService catalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CategoryResult>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<CategoryResult>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        catalogService.ListCategoriesAsync(includeInactive, cancellationToken);

    [HttpGet("{categoryId:guid}")]
    [ProducesResponseType<CategoryResult>(StatusCodes.Status200OK)]
    public Task<CategoryResult> Get(Guid categoryId, CancellationToken cancellationToken) =>
        catalogService.GetCategoryAsync(categoryId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    [ProducesResponseType<CategoryResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CreateCategoryAsync(
            request.ParentCategoryId,
            request.Code,
            request.Name,
            request.Description,
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { categoryId = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{categoryId:guid}")]
    [ProducesResponseType<CategoryResult>(StatusCodes.Status200OK)]
    public Task<CategoryResult> Update(
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken) =>
        catalogService.UpdateCategoryAsync(
            categoryId,
            request.ParentCategoryId,
            request.Name,
            request.Description,
            cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPatch("{categoryId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateStatus(
        Guid categoryId,
        UpdateCategoryStatusRequest request,
        CancellationToken cancellationToken)
    {
        await catalogService.SetCategoryStatusAsync(categoryId, request.IsActive, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpDelete("{categoryId:guid}")]
    [ProducesResponseType<DeleteCategoryResult>(StatusCodes.Status200OK)]
    public Task<DeleteCategoryResult> Delete(Guid categoryId, CancellationToken cancellationToken) =>
        catalogService.DeleteCategoryAsync(categoryId, cancellationToken);
}
