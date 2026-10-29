using FSCM.Application.Features.Promotions;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Promotions;

public sealed class PromotionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Campaign_is_created_as_audited_draft_with_distinct_scopes()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(new("Khuyến mãi sữa tháng mười", 10, 8,
            Now.AddDays(-1), Now.AddDays(30),
            [fixture.Store.Product.Id, fixture.Store.Product.Id],
            [fixture.Store.Category.Id], [fixture.Store.Retailer.Id]),
            fixture.ActorId, CancellationToken.None);

        Assert.Equal("KM-004", result.Code);
        Assert.False(result.IsActive);
        Assert.Single(result.ProductIds);
        Assert.Single(result.CategoryIds);
        Assert.Single(result.RetailerIds);
        Assert.Equal(PromotionAuditActions.Created, fixture.Store.Audits.Single().Action);
    }

    [Fact]
    public async Task Campaign_validity_is_normalized_to_utc_for_postgresql()
    {
        var fixture = new Fixture();
        var vietnamStart = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.FromHours(7));

        var result = await fixture.Service.CreateAsync(new("Khuyến mãi theo giờ Việt Nam", 1, 5,
            vietnamStart, vietnamStart.AddDays(10), [fixture.Store.Product.Id], [], []),
            fixture.ActorId, CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, result.StartAt.Offset);
        Assert.Equal(vietnamStart.UtcDateTime, result.StartAt.UtcDateTime);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    public async Task Batch_discount_enforces_approved_one_to_fifty_percent_range(string rawPercent)
    {
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<PromotionOperationException>(() =>
            fixture.Service.ApplyBatchDiscountAsync(fixture.Store.Batch.Id,
                new(decimal.Parse(rawPercent), Now, Now.AddDays(5)), fixture.ActorId,
                CancellationToken.None));

        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task Batch_discount_rejects_overlapping_validity_period()
    {
        var fixture = new Fixture();
        fixture.Store.AddDiscount(15m, Now.AddDays(-1), Now.AddDays(5));

        var error = await Assert.ThrowsAsync<PromotionOperationException>(() =>
            fixture.Service.ApplyBatchDiscountAsync(fixture.Store.Batch.Id,
                new(10m, Now, Now.AddDays(2)), fixture.ActorId, CancellationToken.None));

        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task Pricing_does_not_stack_and_uses_highest_eligible_discount()
    {
        var fixture = new Fixture();
        fixture.Store.AddCampaign(8m, Now.AddDays(-1), Now.AddDays(5));
        fixture.Store.AddDiscount(15m, Now.AddDays(-1), Now.AddDays(5));

        var result = await fixture.Service.CalculatePriceAsync(fixture.Store.Product.Id, 10,
            fixture.Store.Batch.Id, fixture.Store.Retailer.Id, Now, CancellationToken.None);

        Assert.Equal(2_520_000m, result.GrossAmount);
        Assert.Equal(15m, result.DiscountPercent);
        Assert.Equal(378_000m, result.DiscountAmount);
        Assert.Equal(2_142_000m, result.NetAmount);
        Assert.Equal(PromotionTypes.BatchDiscount, result.AppliedPromotion!.PromotionType);
        Assert.Equal(2, result.EligiblePromotions.Count);
    }

    [Fact]
    public async Task Equal_discount_prefers_batch_specific_rule_over_campaign()
    {
        var fixture = new Fixture();
        fixture.Store.AddCampaign(10m, Now.AddDays(-1), Now.AddDays(5));
        fixture.Store.AddDiscount(10m, Now.AddDays(-1), Now.AddDays(5));

        var result = await fixture.Service.CalculatePriceAsync(fixture.Store.Product.Id, 10,
            fixture.Store.Batch.Id, fixture.Store.Retailer.Id, Now, CancellationToken.None);

        Assert.Equal(PromotionTypes.BatchDiscount, result.AppliedPromotion!.PromotionType);
        Assert.False(result.FinalPricePendingAllocation);
    }

    [Fact]
    public async Task Indicative_quote_uses_only_ordinary_campaign_and_marks_final_price_pending()
    {
        var fixture = new Fixture();
        fixture.Store.AddCampaign(8m, Now.AddDays(-1), Now.AddDays(5));
        fixture.Store.AddDiscount(15m, Now.AddDays(-1), Now.AddDays(5));

        var result = await fixture.Service.CalculateIndicativePriceAsync(fixture.Store.Product.Id, 10,
            fixture.Store.Retailer.Id, Now, CancellationToken.None);

        Assert.Null(result.BatchId);
        Assert.True(result.FinalPricePendingAllocation);
        Assert.Equal(8m, result.DiscountPercent);
        Assert.Equal(PromotionTypes.Campaign, result.AppliedPromotion!.PromotionType);
        Assert.Single(result.EligiblePromotions);
    }

    [Fact]
    public async Task Campaign_minimum_quantity_aggregates_matching_lines_inside_one_order()
    {
        var fixture = new Fixture();
        var campaign = new Promotion
        {
            Id = Guid.NewGuid(), Code = "KM-GROUP", Name = "Nhóm SKU", PromotionType = PromotionTypes.Campaign,
            MinimumQuantity = 10, DiscountPercent = 12, StartAt = Now.AddDays(-1), EndAt = Now.AddDays(5),
            IsActive = true, CreatedAt = Now, UpdatedAt = Now
        };
        fixture.Store.Promotions.Add(campaign);
        fixture.Store.CategoryLinks.Add(new() { Id = Guid.NewGuid(), PromotionId = campaign.Id,
            ProductCategoryId = fixture.Store.Category.Id });

        var result = await fixture.Service.CalculateIndicativePricesAsync(
            [new(fixture.Store.Product.Id, 6), new(fixture.Store.Product2.Id, 4)],
            fixture.Store.Retailer.Id, Now, CancellationToken.None);

        Assert.Equal(12m, result[fixture.Store.Product.Id].DiscountPercent);
        Assert.Equal(12m, result[fixture.Store.Product2.Id].DiscountPercent);
    }

    [Theory]
    [InlineData(30, "15")]
    [InlineData(40, "10")]
    [InlineData(60, "5")]
    [InlineData(61, "0")]
    public async Task Shelf_life_suggestion_uses_approved_thresholds(
        int remainingPercent, string expectedRawDiscount)
    {
        var fixture = new Fixture();
        fixture.Store.Product.ShelfLifeHours = 100 * 24;
        fixture.Store.Batch.ExpirationDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(remainingPercent);

        var result = (await fixture.Service.ListBatchSuggestionsAsync(null, null,
            CancellationToken.None)).Single();

        Assert.Equal(decimal.Parse(expectedRawDiscount), result.SuggestedDiscountPercent);
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public PromotionService Service { get; }

        public Fixture() => Service = new PromotionService(Store, new FixedTimeProvider(Now));
    }

    private sealed class FakeStore : IPromotionStore
    {
        public ProductCategory Category { get; } = new() { Id = Guid.NewGuid(), Code = "CAT-02", Name = "Sữa", IsActive = true };
        public Product Product { get; } = new() { Id = Guid.NewGuid(), Sku = "SKU001", Name = "Sữa tươi", Status = ProductStatuses.Published, ShelfLifeHours = 100 * 24 };
        public Product Product2 { get; } = new() { Id = Guid.NewGuid(), Sku = "SKU002", Name = "Sữa chua", Status = ProductStatuses.Published, ShelfLifeHours = 100 * 24 };
        public Retailer Retailer { get; } = new() { Id = Guid.NewGuid(), Code = "10004127", Name = "Tạp hóa An Khang" };
        public Warehouse Warehouse { get; } = new() { Id = Guid.NewGuid(), Code = "WH-TD", Name = "Kho Thủ Đức", IsActive = true };
        public Batch Batch { get; } = new() { Id = Guid.NewGuid(), BatchCode = "WH-TD-SKU001-31122026", ExpirationDate = new DateOnly(2026, 12, 31) };
        public ProductPrice Price { get; } = new() { Id = Guid.NewGuid(), UnitPrice = 252_000m, IsActive = true, EffectiveFrom = Now.AddMonths(-1) };
        public ProductPrice Price2 { get; } = new() { Id = Guid.NewGuid(), UnitPrice = 100_000m, IsActive = true, EffectiveFrom = Now.AddMonths(-1) };
        public List<Promotion> Promotions { get; } = [];
        public List<PromotionProduct> ProductLinks { get; } = [];
        public List<PromotionCategory> CategoryLinks { get; } = [];
        public List<PromotionRetailer> RetailerLinks { get; } = [];
        public List<BatchPromotion> Assignments { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public FakeStore()
        {
            Product.ProductCategoryId = Category.Id;
            Product2.ProductCategoryId = Category.Id;
            Batch.ProductId = Product.Id;
            Batch.WarehouseId = Warehouse.Id;
            Price.ProductId = Product.Id;
            Price2.ProductId = Product2.Id;
        }

        public void AddCampaign(decimal percent, DateTimeOffset start, DateTimeOffset end)
        {
            var promotion = NewPromotion(PromotionTypes.Campaign, percent, start, end);
            Promotions.Add(promotion);
            ProductLinks.Add(new() { Id = Guid.NewGuid(), PromotionId = promotion.Id, ProductId = Product.Id });
        }

        public void AddDiscount(decimal percent, DateTimeOffset start, DateTimeOffset end)
        {
            var promotion = NewPromotion(PromotionTypes.BatchDiscount, percent, start, end);
            Promotions.Add(promotion);
            Assignments.Add(new()
            {
                Id = Guid.NewGuid(),
                BatchId = Batch.Id,
                PromotionId = promotion.Id,
                AssignedByUserId = Guid.NewGuid(),
                IsActive = true,
                AssignedAt = Now
            });
        }

        public Task<IReadOnlyCollection<PromotionView>> ListPromotionsAsync(bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PromotionView>>(Promotions.Where(item => item.PromotionType == PromotionTypes.Campaign && (includeInactive || item.IsActive)).Select(View).ToArray());
        public Task<PromotionView?> FindPromotionViewAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Promotions.SingleOrDefault(item => item.Id == id) is { } promotion ? View(promotion) : null);
        public Task<Promotion?> FindPromotionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Promotions.SingleOrDefault(item => item.Id == id));
        public Task<string> NextPromotionCodeAsync(CancellationToken cancellationToken) => Task.FromResult($"KM-{Promotions.Count + 4:000}");
        public Task<IReadOnlyCollection<Product>> FindProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Product>>(ids.Contains(Product.Id) ? [Product] : []);
        public Task<IReadOnlyCollection<ProductCategory>> FindCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductCategory>>(ids.Contains(Category.Id) ? [Category] : []);
        public Task<IReadOnlyCollection<Retailer>> FindRetailersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Retailer>>(ids.Contains(Retailer.Id) ? [Retailer] : []);
        public Task<IReadOnlyCollection<PromotionProduct>> ListPromotionProductsAsync(Guid promotionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionProduct>>(ProductLinks.Where(item => item.PromotionId == promotionId).ToArray());
        public Task<IReadOnlyCollection<PromotionCategory>> ListPromotionCategoriesAsync(Guid promotionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionCategory>>(CategoryLinks.Where(item => item.PromotionId == promotionId).ToArray());
        public Task<IReadOnlyCollection<PromotionRetailer>> ListPromotionRetailersAsync(Guid promotionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PromotionRetailer>>(RetailerLinks.Where(item => item.PromotionId == promotionId).ToArray());
        public Task<IReadOnlyCollection<BatchDiscountCandidate>> ListBatchDiscountCandidatesAsync(Guid? warehouseId, Guid? productId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<BatchDiscountCandidate>>([new(Batch, Warehouse, Product, 80m,
                Assignments.Where(item => item.IsActive).Select(item => Promotions.Single(promotion => promotion.Id == item.PromotionId).DiscountPercent).Cast<decimal?>().FirstOrDefault())]);
        public Task<Batch?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<Batch?>(batchId == Batch.Id ? Batch : null);
        public Task<BatchDiscountView?> FindBatchDiscountAsync(Guid id, CancellationToken cancellationToken)
        {
            var assignment = Assignments.SingleOrDefault(item => item.Id == id);
            return Task.FromResult(assignment is null ? null : new BatchDiscountView(assignment,
                Promotions.Single(item => item.Id == assignment.PromotionId), Batch, Warehouse, Product, 80m));
        }
        public Task<IReadOnlyCollection<BatchDiscountView>> ListBatchDiscountsAsync(Guid? warehouseId, Guid? productId, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<BatchDiscountView>>(Assignments.Where(item => includeInactive || item.IsActive).Select(item =>
                new BatchDiscountView(item, Promotions.Single(promotion => promotion.Id == item.PromotionId), Batch, Warehouse, Product, 80m)).ToArray());
        public Task<bool> HasOverlappingBatchDiscountAsync(Guid batchId, DateTimeOffset startAt, DateTimeOffset endAt, Guid? excludingAssignmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.Any(item => item.BatchId == batchId && item.IsActive && item.Id != excludingAssignmentId &&
                Promotions.Single(promotion => promotion.Id == item.PromotionId) is var promotion && promotion.IsActive && promotion.StartAt < endAt && startAt < promotion.EndAt));
        public Task<PriceProductView?> FindPriceProductAsync(Guid productId, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult<PriceProductView?>(productId == Product.Id ? new(Product, Price) :
                productId == Product2.Id ? new(Product2, Price2) : null);
        public Task<Batch?> FindPriceBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult<Batch?>(batchId == Batch.Id ? Batch : null);
        public Task<IReadOnlyCollection<PromotionView>> ListEligibleCampaignsAsync(Guid productId, Guid categoryId, Guid? retailerId, decimal quantity, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PromotionView>>(Promotions.Where(item => item.PromotionType == PromotionTypes.Campaign && item.IsActive && item.StartAt <= at && item.EndAt >= at && item.MinimumQuantity <= quantity &&
                (ProductLinks.Any(link => link.PromotionId == item.Id && link.ProductId == productId) || CategoryLinks.Any(link => link.PromotionId == item.Id && link.ProductCategoryId == categoryId)) &&
                (!RetailerLinks.Any(link => link.PromotionId == item.Id) || RetailerLinks.Any(link => link.PromotionId == item.Id && link.RetailerId == retailerId))).Select(View).ToArray());
        public Task<IReadOnlyCollection<BatchDiscountView>> ListEligibleBatchDiscountsAsync(Guid batchId, decimal quantity, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<BatchDiscountView>>(Assignments.Where(item => item.BatchId == batchId && item.IsActive).Select(item =>
                new BatchDiscountView(item, Promotions.Single(promotion => promotion.Id == item.PromotionId), Batch, Warehouse, Product, 80m))
                .Where(item => item.Promotion.IsActive && item.Promotion.StartAt <= at && item.Promotion.EndAt >= at && item.Promotion.MinimumQuantity <= quantity).ToArray());
        public void AddPromotion(Promotion promotion) => Promotions.Add(promotion);
        public void AddPromotionProduct(PromotionProduct link) => ProductLinks.Add(link);
        public void AddPromotionCategory(PromotionCategory link) => CategoryLinks.Add(link);
        public void AddPromotionRetailer(PromotionRetailer link) => RetailerLinks.Add(link);
        public void AddBatchPromotion(BatchPromotion assignment) => Assignments.Add(assignment);
        public void AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        public void RemovePromotionProducts(IEnumerable<PromotionProduct> links) { foreach (var link in links.ToArray()) ProductLinks.Remove(link); }
        public void RemovePromotionCategories(IEnumerable<PromotionCategory> links) { foreach (var link in links.ToArray()) CategoryLinks.Remove(link); }
        public void RemovePromotionRetailers(IEnumerable<PromotionRetailer> links) { foreach (var link in links.ToArray()) RetailerLinks.Remove(link); }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private PromotionView View(Promotion promotion) => new(promotion,
            ProductLinks.Where(item => item.PromotionId == promotion.Id).Select(item => item.ProductId).ToArray(),
            CategoryLinks.Where(item => item.PromotionId == promotion.Id).Select(item => item.ProductCategoryId).ToArray(),
            RetailerLinks.Where(item => item.PromotionId == promotion.Id).Select(item => item.RetailerId).ToArray());

        private static Promotion NewPromotion(string type, decimal percent, DateTimeOffset start, DateTimeOffset end) => new()
        {
            Id = Guid.NewGuid(),
            Code = $"KM-{Guid.NewGuid().ToString("N")[..3]}",
            Name = "Khuyến mãi thử nghiệm",
            PromotionType = type,
            MinimumQuantity = 1m,
            DiscountPercent = percent,
            StartAt = start,
            EndAt = end,
            IsActive = true,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = Now,
            UpdatedAt = Now
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
