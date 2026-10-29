using FSCM.API.Contracts.Catalog;
using FSCM.Application.Features.Catalog;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/products")]
public sealed class ProductsController(CatalogService catalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CatalogPage<ProductResult>>(StatusCodes.Status200OK)]
    public Task<CatalogPage<ProductResult>> List(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        catalogService.ListProductsAsync(
            new ProductSearch(search, categoryId, status, page, pageSize),
            cancellationToken);

    [HttpGet("{productId:guid}")]
    [ProducesResponseType<ProductResult>(StatusCodes.Status200OK)]
    public Task<ProductResult> Get(Guid productId, CancellationToken cancellationToken) =>
        catalogService.GetProductAsync(productId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost]
    [ProducesResponseType<ProductResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CreateProductAsync(
            new CreateProductData(
                request.ProductCategoryId,
                request.Sku,
                request.Name,
                request.Unit,
                request.ShelfLifeHours,
                request.NearDateThresholdHours,
                request.InitialUnitPrice),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { productId = result.Id }, result);
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPut("{productId:guid}")]
    [ProducesResponseType<ProductResult>(StatusCodes.Status200OK)]
    public Task<ProductResult> Update(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken) =>
        catalogService.UpdateProductAsync(
            productId,
            new UpdateProductData(
                request.ProductCategoryId,
                request.Name,
                request.Unit,
                request.ShelfLifeHours,
                request.NearDateThresholdHours),
            cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid productId, CancellationToken cancellationToken)
    {
        await catalogService.DeleteDraftProductAsync(productId, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{productId:guid}/publish")]
    [ProducesResponseType<ProductResult>(StatusCodes.Status200OK)]
    public Task<ProductResult> Publish(Guid productId, CancellationToken cancellationToken) =>
        catalogService.PublishProductAsync(productId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{productId:guid}/inactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Inactivate(Guid productId, CancellationToken cancellationToken)
    {
        await catalogService.InactivateProductAsync(productId, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{productId:guid}/replacement")]
    [ProducesResponseType<ProductResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReplacement(
        Guid productId,
        CreateReplacementProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CreateReplacementAsync(
            productId,
            new CreateReplacementProductData(
                request.Sku,
                request.Name,
                request.Unit,
                request.ShelfLifeHours,
                request.NearDateThresholdHours,
                request.InitialUnitPrice),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { productId = result.Id }, result);
    }

    /// <summary>
    /// Copies allowed product master data into an independent Draft with a caller-supplied new SKU.
    /// Inventory, batches, reservations, transactions, and replacement lineage are never copied.
    /// </summary>
    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{productId:guid}/clone")]
    [ProducesResponseType<ProductResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Clone(
        Guid productId,
        CloneProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CloneProductAsync(
            productId,
            new CloneProductData(
                request.Sku,
                request.ProductCategoryId,
                request.Name,
                request.Unit,
                request.ShelfLifeHours,
                request.NearDateThresholdHours,
                request.InitialUnitPrice),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { productId = result.Id }, result);
    }

    [HttpGet("{productId:guid}/prices")]
    [ProducesResponseType<IReadOnlyCollection<ProductPriceResult>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<ProductPriceResult>> ListPrices(
        Guid productId,
        CancellationToken cancellationToken) =>
        catalogService.ListPriceHistoryAsync(productId, cancellationToken);

    [HttpGet("{productId:guid}/price-suggestion")]
    [ProducesResponseType<ProductPriceSuggestion>(StatusCodes.Status200OK)]
    public Task<ProductPriceSuggestion> GetPriceSuggestion(
        Guid productId,
        CancellationToken cancellationToken) =>
        catalogService.GetPriceSuggestionAsync(productId, cancellationToken);

    [Authorize(Roles = SystemRoles.Admin)]
    [HttpPost("{productId:guid}/prices")]
    [ProducesResponseType<ProductPriceResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> SetPrice(
        Guid productId,
        SetProductPriceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.SetPriceAsync(productId, request.UnitPrice, cancellationToken);
        return CreatedAtAction(nameof(ListPrices), new { productId }, result);
    }
}
