using FSCM.Application.Features.Catalog;
using FSCM.Application.Features.InternalTransfers;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.Territory;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using HotChocolate.Authorization;
using HotChocolate.Types;

namespace FSCM.API.GraphQL;

public sealed record CategoryReference(Guid Id, string Code, string Name);
public sealed record ProductReference(Guid Id, string Sku, string Name);
public sealed record WarehouseReference(Guid Id, string Code, string Name);
public sealed record BatchReference(Guid Id, string Code, DateOnly? ExpirationDate = null);
public sealed record PriceValue(decimal Amount);

[ExtendObjectType<ProductResult>]
public sealed class ProductGraphQlExtensions
{
    public CategoryReference Category([Parent] ProductResult product) =>
        new(product.ProductCategoryId, product.CategoryCode, product.CategoryName);

    public PriceValue? CurrentPrice([Parent] ProductResult product) =>
        product.CurrentUnitPrice is { } amount ? new(amount) : null;

    public async Task<IReadOnlyList<ProductPriceResult>> Prices(
        [Parent] ProductResult product,
        IPricesByProductIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(product.Id, cancellationToken) ?? [];

    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    public async Task<IReadOnlyList<BatchInventoryResult>> Inventory(
        [Parent] ProductResult product,
        IInventoryByProductIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(product.Id, cancellationToken) ?? [];

    public async Task<IReadOnlyList<MlorResult>> MlorConfigurations(
        [Parent] ProductResult product,
        IMlorByProductIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(product.Id, cancellationToken) ?? [];
}

[ExtendObjectType<WarehouseResult>]
public sealed class WarehouseGraphQlExtensions
{
    [Authorize(Roles = [SystemRoles.Admin, SystemRoles.Operator, SystemRoles.WarehouseKeeper])]
    public async Task<IReadOnlyList<BatchInventoryResult>> Inventory(
        [Parent] WarehouseResult warehouse,
        IInventoryByWarehouseIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(warehouse.Id, cancellationToken) ?? [];

    public async Task<IReadOnlyList<TransitTimeRuleResult>> TransitTimeRules(
        [Parent] WarehouseResult warehouse,
        ITransitTimeRulesByWarehouseIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(warehouse.Id, cancellationToken) ?? [];
}

[ExtendObjectType<RetailerDetails>]
public sealed class RetailerGraphQlExtensions
{
    public async Task<RegionResult?> Region(
        [Parent] RetailerDetails retailer,
        IRegionByIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(retailer.RegionId, cancellationToken);

    public async Task<SalesGroupResult?> SalesGroup(
        [Parent] RetailerDetails retailer,
        ISalesGroupByIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(retailer.SalesGroupId, cancellationToken);

    public async Task<IReadOnlyList<MlorResult>> MlorConfigurations(
        [Parent] RetailerDetails retailer,
        IMlorByRetailerIdDataLoader loader,
        CancellationToken cancellationToken) =>
        await loader.LoadAsync(retailer.Id, cancellationToken) ?? [];
}

[ExtendObjectType<BatchInventoryResult>]
public sealed class InventoryGraphQlExtensions
{
    public ProductReference Product([Parent] BatchInventoryResult inventory) =>
        new(inventory.ProductId, inventory.Sku, inventory.ProductName);

    public WarehouseReference Warehouse([Parent] BatchInventoryResult inventory) =>
        new(inventory.WarehouseId, inventory.WarehouseCode, inventory.WarehouseName);

    public BatchReference Batch([Parent] BatchInventoryResult inventory) =>
        new(inventory.BatchId, inventory.BatchCode, inventory.ExpirationDate);
}

[ExtendObjectType<GoodsReceiptResult>]
public sealed class GoodsReceiptGraphQlExtensions
{
    public WarehouseReference Warehouse([Parent] GoodsReceiptResult receipt) =>
        new(receipt.WarehouseId, receipt.WarehouseCode, receipt.WarehouseName);
}

[ExtendObjectType<GoodsReceiptLineResult>]
public sealed class GoodsReceiptLineGraphQlExtensions
{
    public ProductReference Product([Parent] GoodsReceiptLineResult line) =>
        new(line.ProductId, line.Sku, line.ProductName);

    public BatchReference Batch([Parent] GoodsReceiptLineResult line) =>
        new(line.BatchId, line.BatchCode, line.ExpirationDate);
}

[ExtendObjectType<QrLabelResult>]
public sealed class QrLabelGraphQlExtensions
{
    public ProductReference Product([Parent] QrLabelResult label) =>
        new(label.ProductId, label.Sku, label.Sku);

    public WarehouseReference Warehouse([Parent] QrLabelResult label) =>
        new(label.WarehouseId, label.WarehouseCode, label.WarehouseCode);

    public BatchReference Batch([Parent] QrLabelResult label) =>
        new(label.BatchId, label.BatchCode);
}

[ExtendObjectType<InternalTransferResult>]
public sealed class InternalTransferGraphQlExtensions
{
    public WarehouseReference SourceWarehouse([Parent] InternalTransferResult transfer) =>
        new(transfer.SourceWarehouseId, transfer.SourceWarehouseCode, transfer.SourceWarehouseName);

    public WarehouseReference DestinationWarehouse([Parent] InternalTransferResult transfer) =>
        new(transfer.DestinationWarehouseId, transfer.DestinationWarehouseCode, transfer.DestinationWarehouseName);
}

[ExtendObjectType<InternalTransferItemResult>]
public sealed class InternalTransferItemGraphQlExtensions
{
    public ProductReference Product([Parent] InternalTransferItemResult item) =>
        new(item.ProductId, item.Sku, item.ProductName);

    public BatchReference SourceBatch([Parent] InternalTransferItemResult item) =>
        new(item.SourceBatchId, item.SourceBatchCode, item.ExpirationDate);

    public BatchReference? DestinationBatch([Parent] InternalTransferItemResult item) =>
        item.DestinationBatchId is { } id && item.DestinationBatchCode is { } code
            ? new(id, code, item.ExpirationDate)
            : null;
}
