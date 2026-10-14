using FSCM.Application.Features.Promotions;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace FSCM.Persistence.Promotions;

public sealed class PromotionStore(AppDbContext dbContext) : IPromotionStore
{
    public async Task<IReadOnlyCollection<PromotionView>> ListPromotionsAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.PromotionRecords.AsNoTracking()
            .Where(item => item.PromotionType == PromotionTypes.Campaign);
        if (!includeInactive) query = query.Where(item => item.IsActive);
        var promotions = await query.OrderByDescending(item => item.StartAt)
            .ThenBy(item => item.Code).ToArrayAsync(cancellationToken);
        return await CreateViewsAsync(promotions, cancellationToken);
    }

    public async Task<PromotionView?> FindPromotionViewAsync(Guid id, CancellationToken cancellationToken)
    {
        var promotion = await dbContext.PromotionRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return promotion is null ? null : (await CreateViewsAsync([promotion], cancellationToken)).Single();
    }

    public Task<Promotion?> FindPromotionAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PromotionRecords.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<string> NextPromotionCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"PromotionCodeSequence\"');";
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            if (number > 999) throw PromotionOperationException.Conflict("Promotion code range is exhausted.");
            return $"KM-{number:000}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public async Task<IReadOnlyCollection<Product>> FindProductsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.ProductRecords.AsNoTracking().Where(item => ids.Contains(item.Id))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ProductCategory>> FindCategoriesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.ProductCategoryRecords.AsNoTracking().Where(item => ids.Contains(item.Id))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Retailer>> FindRetailersAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.RetailerRecords.AsNoTracking().Where(item => ids.Contains(item.Id))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PromotionProduct>> ListPromotionProductsAsync(
        Guid promotionId, CancellationToken cancellationToken) =>
        await dbContext.PromotionProductRecords.Where(item => item.PromotionId == promotionId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PromotionCategory>> ListPromotionCategoriesAsync(
        Guid promotionId, CancellationToken cancellationToken) =>
        await dbContext.PromotionCategoryRecords.Where(item => item.PromotionId == promotionId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PromotionRetailer>> ListPromotionRetailersAsync(
        Guid promotionId, CancellationToken cancellationToken) =>
        await dbContext.PromotionRetailerRecords.Where(item => item.PromotionId == promotionId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<BatchDiscountCandidate>> ListBatchDiscountCandidatesAsync(
        Guid? warehouseId, Guid? productId, CancellationToken cancellationToken)
    {
        var query =
            from batch in dbContext.BatchRecords.AsNoTracking()
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
            join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            where (!warehouseId.HasValue || batch.WarehouseId == warehouseId.Value) &&
                  (!productId.HasValue || batch.ProductId == productId.Value)
            orderby batch.ExpirationDate, batch.BatchCode
            select new BatchDiscountCandidate(batch, warehouse, product, balance.AvailableQuantity,
                (from assignment in dbContext.BatchPromotionRecords.AsNoTracking()
                 join promotion in dbContext.PromotionRecords.AsNoTracking()
                     on assignment.PromotionId equals promotion.Id
                 where assignment.BatchId == batch.Id && assignment.IsActive && promotion.IsActive
                 orderby promotion.DiscountPercent descending
                 select (decimal?)promotion.DiscountPercent).FirstOrDefault());
        return await query.ToArrayAsync(cancellationToken);
    }

    public Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.BatchRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);

    public async Task<BatchDiscountView?> FindBatchDiscountAsync(Guid id, CancellationToken cancellationToken)
    {
        var query =
            from assignment in dbContext.BatchPromotionRecords
            join promotion in dbContext.PromotionRecords on assignment.PromotionId equals promotion.Id
            join batch in dbContext.BatchRecords on assignment.BatchId equals batch.Id
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
            join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            where assignment.Id == id
            select new BatchDiscountView(assignment, promotion, batch, warehouse, product, balance.AvailableQuantity);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<BatchDiscountView>> ListBatchDiscountsAsync(
        Guid? warehouseId, Guid? productId, bool includeInactive, CancellationToken cancellationToken)
    {
        var query =
            from assignment in dbContext.BatchPromotionRecords.AsNoTracking()
            join promotion in dbContext.PromotionRecords.AsNoTracking() on assignment.PromotionId equals promotion.Id
            join batch in dbContext.BatchRecords.AsNoTracking() on assignment.BatchId equals batch.Id
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
            join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            where promotion.PromotionType == PromotionTypes.BatchDiscount &&
                  (!warehouseId.HasValue || batch.WarehouseId == warehouseId.Value) &&
                  (!productId.HasValue || batch.ProductId == productId.Value) &&
                  (includeInactive || (assignment.IsActive && promotion.IsActive))
            orderby promotion.StartAt descending, batch.BatchCode
            select new BatchDiscountView(assignment, promotion, batch, warehouse, product, balance.AvailableQuantity);
        return await query.ToArrayAsync(cancellationToken);
    }

    public Task<bool> HasOverlappingBatchDiscountAsync(
        Guid batchId, DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludingAssignmentId,
        CancellationToken cancellationToken) =>
        (from assignment in dbContext.BatchPromotionRecords
         join promotion in dbContext.PromotionRecords on assignment.PromotionId equals promotion.Id
         where assignment.BatchId == batchId && assignment.IsActive && promotion.IsActive &&
               (!excludingAssignmentId.HasValue || assignment.Id != excludingAssignmentId.Value) &&
               promotion.StartAt < endAt && startAt < promotion.EndAt
         select assignment).AnyAsync(cancellationToken);

    public async Task<PriceProductView?> FindPriceProductAsync(
        Guid productId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var query =
            from product in dbContext.ProductRecords.AsNoTracking()
            join price in dbContext.ProductPriceRecords.AsNoTracking() on product.Id equals price.ProductId
            where product.Id == productId && product.Status == ProductStatuses.Published &&
                  price.IsActive && price.EffectiveFrom <= at &&
                  (!price.EffectiveTo.HasValue || price.EffectiveTo.Value > at)
            orderby price.EffectiveFrom descending
            select new PriceProductView(product, price);
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Batch?> FindPriceBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        dbContext.BatchRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);

    public async Task<IReadOnlyCollection<PromotionView>> ListEligibleCampaignsAsync(
        Guid productId, Guid categoryId, Guid? retailerId, decimal quantity,
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        var query = dbContext.PromotionRecords.AsNoTracking().Where(promotion =>
            promotion.PromotionType == PromotionTypes.Campaign && promotion.IsActive &&
            promotion.StartAt <= at && promotion.EndAt >= at && promotion.MinimumQuantity <= quantity &&
            (dbContext.PromotionProductRecords.Any(link => link.PromotionId == promotion.Id && link.ProductId == productId) ||
             dbContext.PromotionCategoryRecords.Any(link => link.PromotionId == promotion.Id && link.ProductCategoryId == categoryId)) &&
            (!dbContext.PromotionRetailerRecords.Any(link => link.PromotionId == promotion.Id) ||
             (retailerId.HasValue && dbContext.PromotionRetailerRecords.Any(link =>
                 link.PromotionId == promotion.Id && link.RetailerId == retailerId.Value))));
        return await CreateViewsAsync(await query.ToArrayAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyCollection<BatchDiscountView>> ListEligibleBatchDiscountsAsync(
        Guid batchId, decimal quantity, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var query =
            from assignment in dbContext.BatchPromotionRecords.AsNoTracking()
            join promotion in dbContext.PromotionRecords.AsNoTracking() on assignment.PromotionId equals promotion.Id
            join batch in dbContext.BatchRecords.AsNoTracking() on assignment.BatchId equals batch.Id
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
            join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            where assignment.BatchId == batchId && assignment.IsActive && promotion.IsActive &&
                  promotion.PromotionType == PromotionTypes.BatchDiscount &&
                  promotion.StartAt <= at && promotion.EndAt >= at && promotion.MinimumQuantity <= quantity
            select new BatchDiscountView(assignment, promotion, batch, warehouse, product, balance.AvailableQuantity);
        return await query.ToArrayAsync(cancellationToken);
    }

    public void AddPromotion(Promotion promotion) => dbContext.PromotionRecords.Add(promotion);
    public void AddPromotionProduct(PromotionProduct link) => dbContext.PromotionProductRecords.Add(link);
    public void AddPromotionCategory(PromotionCategory link) => dbContext.PromotionCategoryRecords.Add(link);
    public void AddPromotionRetailer(PromotionRetailer link) => dbContext.PromotionRetailerRecords.Add(link);
    public void AddBatchPromotion(BatchPromotion assignment) => dbContext.BatchPromotionRecords.Add(assignment);
    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);
    public void RemovePromotionProducts(IEnumerable<PromotionProduct> links) => dbContext.PromotionProductRecords.RemoveRange(links);
    public void RemovePromotionCategories(IEnumerable<PromotionCategory> links) => dbContext.PromotionCategoryRecords.RemoveRange(links);
    public void RemovePromotionRetailers(IEnumerable<PromotionRetailer> links) => dbContext.PromotionRetailerRecords.RemoveRange(links);
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw PromotionOperationException.Conflict(
                "A concurrent request created the same promotion assignment. Refresh and retry.");
        }
    }

    private async Task<IReadOnlyCollection<PromotionView>> CreateViewsAsync(
        IReadOnlyCollection<Promotion> promotions, CancellationToken cancellationToken)
    {
        if (promotions.Count == 0) return [];
        var ids = promotions.Select(item => item.Id).ToArray();
        var products = await dbContext.PromotionProductRecords.AsNoTracking()
            .Where(item => ids.Contains(item.PromotionId)).ToArrayAsync(cancellationToken);
        var categories = await dbContext.PromotionCategoryRecords.AsNoTracking()
            .Where(item => ids.Contains(item.PromotionId)).ToArrayAsync(cancellationToken);
        var retailers = await dbContext.PromotionRetailerRecords.AsNoTracking()
            .Where(item => ids.Contains(item.PromotionId)).ToArrayAsync(cancellationToken);
        return promotions.Select(promotion => new PromotionView(promotion,
            products.Where(item => item.PromotionId == promotion.Id).Select(item => item.ProductId).ToArray(),
            categories.Where(item => item.PromotionId == promotion.Id).Select(item => item.ProductCategoryId).ToArray(),
            retailers.Where(item => item.PromotionId == promotion.Id).Select(item => item.RetailerId).ToArray())).ToArray();
    }
}
