using FSCM.Application.Features.MobileSnapshots;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FSCM.Persistence.MobileSnapshots;

public sealed class MobileSnapshotStore(AppDbContext dbContext) : IMobileSnapshotStore
{
    public async Task<MobileSnapshotData> LoadAsync(
        Guid salesUserId, DateTimeOffset? changedAfter, CancellationToken cancellationToken)
    {
        var retailerIds = await (
            from assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking()
            join member in dbContext.SalesGroupMemberRecords.AsNoTracking()
                on assignment.SalesGroupId equals member.SalesGroupId
            where assignment.IsActive && member.IsActive && member.UserAccountId == salesUserId
            select assignment.RetailerId).Distinct().ToArrayAsync(cancellationToken);

        var retailerQuery = dbContext.RetailerRecords.AsNoTracking().Where(x => retailerIds.Contains(x.Id));
        var categoryQuery = dbContext.ProductCategoryRecords.AsNoTracking();
        var productQuery = dbContext.ProductRecords.AsNoTracking();
        var priceQuery = dbContext.ProductPriceRecords.AsNoTracking();
        var promotionQuery = dbContext.PromotionRecords.AsNoTracking();
        var mlorQuery = dbContext.RetailerProductMlorRecords.AsNoTracking()
            .Where(x => retailerIds.Contains(x.RetailerId));
        if (changedAfter.HasValue)
        {
            var since = changedAfter.Value;
            retailerQuery = retailerQuery.Where(x => x.UpdatedAt > since);
            categoryQuery = categoryQuery.Where(x => x.UpdatedAt > since);
            productQuery = productQuery.Where(x => x.UpdatedAt > since);
            priceQuery = priceQuery.Where(x => x.CreatedAt > since || x.EffectiveTo > since);
            promotionQuery = promotionQuery.Where(x => x.UpdatedAt > since);
            mlorQuery = mlorQuery.Where(x => x.EffectiveFrom > since || x.EffectiveTo > since);
        }

        var retailers = await retailerQuery.OrderBy(x => x.Code).Select(x => new MobileSnapshotRetailer(
            x.Id, x.Code, x.Name, x.Address, x.Status, x.CreditTermDays, x.UpdatedAt)).ToArrayAsync(cancellationToken);
        var categories = await categoryQuery.OrderBy(x => x.Code).Select(x => new MobileSnapshotCategory(
            x.Id, x.ParentCategoryId, x.Code, x.Name, x.IsActive, x.UpdatedAt)).ToArrayAsync(cancellationToken);
        var products = await productQuery.OrderBy(x => x.Sku).Select(x => new MobileSnapshotProduct(
            x.Id, x.ProductCategoryId, x.Sku, x.Name, x.Unit, x.ShelfLifeHours, x.Status, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        var prices = await priceQuery.OrderBy(x => x.ProductId).ThenBy(x => x.EffectiveFrom)
            .Select(x => new MobileSnapshotPrice(x.Id, x.ProductId, x.UnitPrice, x.EffectiveFrom,
                x.EffectiveTo, x.IsActive, x.CreatedAt)).ToArrayAsync(cancellationToken);
        var promotionEntities = await promotionQuery.OrderBy(x => x.Code).ToArrayAsync(cancellationToken);
        var promotionIds = promotionEntities.Select(x => x.Id).ToArray();
        var promotionProducts = await dbContext.PromotionProductRecords.AsNoTracking()
            .Where(x => promotionIds.Contains(x.PromotionId)).ToArrayAsync(cancellationToken);
        var promotionCategories = await dbContext.PromotionCategoryRecords.AsNoTracking()
            .Where(x => promotionIds.Contains(x.PromotionId)).ToArrayAsync(cancellationToken);
        var promotionRetailers = await dbContext.PromotionRetailerRecords.AsNoTracking()
            .Where(x => promotionIds.Contains(x.PromotionId) && retailerIds.Contains(x.RetailerId))
            .ToArrayAsync(cancellationToken);
        var promotions = promotionEntities.Select(x => new MobileSnapshotPromotion(
            x.Id, x.Code, x.Name, x.PromotionType, x.MinimumQuantity, x.DiscountPercent,
            x.StartAt, x.EndAt, x.IsActive,
            promotionProducts.Where(y => y.PromotionId == x.Id).Select(y => y.ProductId).ToArray(),
            promotionCategories.Where(y => y.PromotionId == x.Id).Select(y => y.ProductCategoryId).ToArray(),
            promotionRetailers.Where(y => y.PromotionId == x.Id).Select(y => y.RetailerId).ToArray(),
            x.UpdatedAt)).ToArray();
        var mlors = await mlorQuery.OrderBy(x => x.RetailerId).ThenBy(x => x.Version)
            .Select(x => new MobileSnapshotMlor(x.Id, x.RetailerId, x.ProductCategoryId, x.ProductId,
                x.MinimumLifeRatio, x.MinimumLifeHours, x.Version, x.EffectiveFrom, x.EffectiveTo,
                x.IsActive, x.EffectiveTo ?? x.EffectiveFrom)).ToArrayAsync(cancellationToken);

        var stockQuery = from balance in dbContext.InventoryBalanceRecords.AsNoTracking()
                         join batch in dbContext.BatchRecords.AsNoTracking() on balance.BatchId equals batch.Id
                         join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
                         where warehouse.IsActive
                         select new { batch.ProductId, balance.AvailableQuantity, balance.UpdatedAt };
        if (changedAfter.HasValue) stockQuery = stockQuery.Where(x => x.UpdatedAt > changedAfter.Value);
        var stocks = await stockQuery.GroupBy(x => x.ProductId).Select(group => new MobileSnapshotStock(
            group.Key, group.Sum(x => x.AvailableQuantity), group.Max(x => x.UpdatedAt)))
            .OrderBy(x => x.ProductId).ToArrayAsync(cancellationToken);

        var defaultMlorEntity = await dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(x => x.ConfigKey == "mlor.default_ratio" && x.IsActive)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
        MobileSnapshotDefaultMlor? defaultMlor = null;
        if (defaultMlorEntity is not null &&
            (!changedAfter.HasValue || defaultMlorEntity.UpdatedAt > changedAfter.Value))
        {
            using var document = JsonDocument.Parse(defaultMlorEntity.ConfigValue);
            if (document.RootElement.TryGetDecimal(out var ratio))
                defaultMlor = new(ratio, defaultMlorEntity.Version, defaultMlorEntity.EffectiveFrom,
                    defaultMlorEntity.UpdatedAt);
        }

        var timestamps = retailers.Select(x => x.UpdatedAt).Concat(categories.Select(x => x.UpdatedAt))
            .Concat(products.Select(x => x.UpdatedAt))
            .Concat(prices.Select(x => x.UpdatedAt)).Concat(promotions.Select(x => x.UpdatedAt))
            .Concat(mlors.Select(x => x.UpdatedAt)).Concat(stocks.Select(x => x.UpdatedAt))
            .Concat(defaultMlor is null ? [] : [defaultMlor.UpdatedAt]).ToArray();
        var latest = timestamps.Length == 0 ? changedAfter ?? DateTimeOffset.UnixEpoch : timestamps.Max();
        return new(retailers, categories, products, prices, promotions, mlors, stocks, defaultMlor, latest);
    }
}
