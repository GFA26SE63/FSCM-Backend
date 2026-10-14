using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Promotions;

public sealed record PromotionView(
    Promotion Promotion, IReadOnlyCollection<Guid> ProductIds,
    IReadOnlyCollection<Guid> CategoryIds, IReadOnlyCollection<Guid> RetailerIds);

public sealed record BatchDiscountView(
    BatchPromotion Assignment, Promotion Promotion, Batch Batch,
    Warehouse Warehouse, Product Product, decimal AvailableQuantity);

public sealed record BatchDiscountCandidate(
    Batch Batch, Warehouse Warehouse, Product Product,
    decimal AvailableQuantity, decimal? CurrentDiscountPercent);

public sealed record PriceProductView(Product Product, ProductPrice Price);

public interface IPromotionStore
{
    Task<IReadOnlyCollection<PromotionView>> ListPromotionsAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<PromotionView?> FindPromotionViewAsync(Guid id, CancellationToken cancellationToken);
    Task<Promotion?> FindPromotionAsync(Guid id, CancellationToken cancellationToken);
    Task<string> NextPromotionCodeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Product>> FindProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductCategory>> FindCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Retailer>> FindRetailersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PromotionProduct>> ListPromotionProductsAsync(Guid promotionId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PromotionCategory>> ListPromotionCategoriesAsync(Guid promotionId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PromotionRetailer>> ListPromotionRetailersAsync(Guid promotionId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BatchDiscountCandidate>> ListBatchDiscountCandidatesAsync(Guid? warehouseId, Guid? productId, CancellationToken cancellationToken);
    Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken);
    Task<BatchDiscountView?> FindBatchDiscountAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BatchDiscountView>> ListBatchDiscountsAsync(Guid? warehouseId, Guid? productId, bool includeInactive, CancellationToken cancellationToken);
    Task<bool> HasOverlappingBatchDiscountAsync(Guid batchId, DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludingAssignmentId, CancellationToken cancellationToken);
    Task<PriceProductView?> FindPriceProductAsync(Guid productId, DateTimeOffset at, CancellationToken cancellationToken);
    Task<Batch?> FindPriceBatchAsync(Guid batchId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PromotionView>> ListEligibleCampaignsAsync(Guid productId, Guid categoryId, Guid? retailerId, decimal quantity, DateTimeOffset at, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BatchDiscountView>> ListEligibleBatchDiscountsAsync(Guid batchId, decimal quantity, DateTimeOffset at, CancellationToken cancellationToken);
    void AddPromotion(Promotion promotion);
    void AddPromotionProduct(PromotionProduct link);
    void AddPromotionCategory(PromotionCategory link);
    void AddPromotionRetailer(PromotionRetailer link);
    void AddBatchPromotion(BatchPromotion assignment);
    void AddAuditLog(AuditLog auditLog);
    void RemovePromotionProducts(IEnumerable<PromotionProduct> links);
    void RemovePromotionCategories(IEnumerable<PromotionCategory> links);
    void RemovePromotionRetailers(IEnumerable<PromotionRetailer> links);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
