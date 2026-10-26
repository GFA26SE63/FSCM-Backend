using FSCM.Application.Features.Identity;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Retailers;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Promotions;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FSCM.Persistence.Seeding;

public sealed class DevelopmentDataSeeder(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    TimeProvider timeProvider,
    OrderService orderService,
    IPaymentPlanService paymentPlanService,
    ILogger<DevelopmentDataSeeder> logger)
{
    private const string DemoWarehouseCode = "WH-HCM-01";
    private const string DemoRegionCode = "HCM";
    private const string DemoSalesGroupCode = "HCM-A";
    private const string DemoRetailerCode = "10004127";
    private const string LegacyDemoRetailerCode = "RTL-HCM-001";
    private static readonly Guid DemoOrderClientKey = Guid.Parse("10000000-0000-0000-0000-000000001030");

    private static readonly (string Email, string FullName, string Role)[] DemoUsers =
    [
        ("admin@fscm.local", "Quản trị viên FSCM", SystemRoles.Admin),
        ("operator@fscm.local", "Nhân viên vận hành FSCM", SystemRoles.Operator),
        ("sales.manager@fscm.local", "Quản lý bán hàng FSCM", SystemRoles.SalesManager),
        ("sales@fscm.local", "Nhân viên bán hàng FSCM", SystemRoles.Sales),
        ("warehouse.keeper@fscm.local", "Thủ kho FSCM", SystemRoles.WarehouseKeeper),
        ("retailer@fscm.local", "Người dùng nhà bán lẻ FSCM", SystemRoles.Retailer)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var password = configuration["SeedData:DefaultPassword"];
        ValidatePassword(password);
        await dbContext.Database.MigrateAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var users = await SeedUsersAsync(password!, now, cancellationToken);
        var regions = await SeedRegionsAsync(now, cancellationToken);
        var warehouse = await SeedWarehouseAsync(now, cancellationToken);
        var salesGroups = await SeedSalesGroupsAsync(regions, now, cancellationToken);
        var retailer = await SeedRetailerAsync(regions[DemoRegionCode].Id, users[SystemRoles.Sales].Id,
            users[SystemRoles.Admin].Id, users[SystemRoles.Retailer].Id, now, cancellationToken);
        var categories = await SeedCategoriesAsync(now, cancellationToken);
        var products = await SeedProductsAsync(categories, now, cancellationToken);
        var configuredWarehouses = await SeedWarehouseConfigurationDataAsync(
            warehouse, retailer, products, users[SystemRoles.Admin].Id, now, cancellationToken);

        foreach (var configuredWarehouse in configuredWarehouses)
        {
            await SeedWarehouseAssignmentsAsync(users, configuredWarehouse.Id, now, cancellationToken);
        }
        await SeedDeliveryMasterDataAsync(configuredWarehouses, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SeedInventoryDataAsync(configuredWarehouses, products, users[SystemRoles.WarehouseKeeper].Id, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SeedQrLabelDataAsync(users[SystemRoles.WarehouseKeeper].Id, now, cancellationToken);
        await SeedSalesGroupMembershipsAsync(users, salesGroups, now, cancellationToken);
        await SeedRetailerAssignmentsAsync(retailer.Id, salesGroups, now, cancellationToken);
        await SeedAdditionalRetailersAsync(
            regions, salesGroups, users, categories["CAT-01"].Id, now, cancellationToken);
        await SeedPromotionDataAsync(products, categories, retailer, users[SystemRoles.Admin].Id,
            now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        var demoOrder = await orderService.CreateAsync(
            new CreateAssistedOrderData(
                DemoOrderClientKey,
                retailer.Id,
                OrderPaymentMethods.Cod,
                null,
                null,
                "Đơn hàng hỗ trợ mẫu cho cửa hàng An Khang",
                [new CreateOrderLineData(products["SKU001"].Id, 12)]),
            users[SystemRoles.Sales].Id,
            cancellationToken);
        var paymentSeedReference = "CHỜ-PHÂN-BỔ";
        if (!demoOrder.FinalPricePendingAllocation)
        {
            if (demoOrder.Status is OrderStatuses.PendingApproval)
            {
                demoOrder = await orderService.ApproveAsync(
                    demoOrder.Id, demoOrder.Version, users[SystemRoles.Operator].Id,
                    SystemRoles.Operator, cancellationToken);
            }
            paymentSeedReference = $"SEED-S1E-{demoOrder.OrderCode}";
            var existingPayment = await dbContext.PaymentRecords.SingleOrDefaultAsync(
                item => item.SalesOrderId == demoOrder.Id, cancellationToken);
            if (existingPayment is null)
            {
                await paymentPlanService.CreateForFinalApprovedOrderAsync(
                    await dbContext.SalesOrderRecords.SingleAsync(
                        item => item.Id == demoOrder.Id, cancellationToken),
                    users[SystemRoles.Admin].Id,
                    now,
                    paymentSeedReference,
                    cancellationToken);
            }
            else
            {
                if (existingPayment.Method != demoOrder.PaymentMethod ||
                    existingPayment.Amount != demoOrder.FinalAmount)
                    throw new InvalidOperationException(
                        "Sổ thanh toán mẫu không khớp phương thức hoặc giá trị cuối của đơn hàng mẫu.");
                paymentSeedReference = existingPayment.FinalApprovalReference;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Development data seeded: {UserCount} users, {CategoryCount} categories, {ProductCount} products, {WarehouseCount} warehouses, {RegionCount} regions, {SalesGroupCount} sales groups, retailer {RetailerCode}, assisted order {OrderCode}, payment state {PaymentReference}.",
            users.Count,
            categories.Count,
            products.Count,
            configuredWarehouses.Count,
            regions.Count,
            salesGroups.Count,
            DemoRetailerCode,
            demoOrder.OrderCode,
            paymentSeedReference);
    }

    private async Task SeedDeliveryMasterDataAsync(
        IReadOnlyCollection<Warehouse> warehouses,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new { WarehouseCode = "WH-HCM-01", DriverCode = "NG-010", DriverName = "Nguyễn Văn Minh",
                Phone = "0901000010", License = "GPLX-C-010", Plate = "51C-482.17", Type = "Xe tải 2.5 tấn", Capacity = 500m },
            new { WarehouseCode = "WH-TD", DriverCode = "NG-011", DriverName = "Trần Quốc Bảo",
                Phone = "0901000011", License = "GPLX-C-011", Plate = "51D-318.26", Type = "Xe tải 1.5 tấn", Capacity = 300m },
            new { WarehouseCode = "WH-Q7", DriverCode = "NG-012", DriverName = "Lê Hoàng Nam",
                Phone = "0901000012", License = "GPLX-C-012", Plate = "51D-725.39", Type = "Xe tải 1.5 tấn", Capacity = 300m }
        };
        var warehousesByCode = warehouses.ToDictionary(item => item.Code, StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var warehouse = warehousesByCode[definition.WarehouseCode];
            var driver = await dbContext.DriverRecords.SingleOrDefaultAsync(
                item => item.Code == definition.DriverCode, cancellationToken);
            if (driver is null)
            {
                driver = new Driver { Id = Guid.NewGuid(), Code = definition.DriverCode, CreatedAt = now };
                dbContext.DriverRecords.Add(driver);
            }
            driver.WarehouseId = warehouse.Id;
            driver.FullName = definition.DriverName;
            driver.Phone = definition.Phone;
            driver.LicenseNumber = definition.License;
            driver.IsActive = true;
            driver.UpdatedAt = now;

            var vehicle = await dbContext.VehicleRecords.SingleOrDefaultAsync(
                item => item.PlateNumber == definition.Plate, cancellationToken);
            if (vehicle is null)
            {
                vehicle = new Vehicle { Id = Guid.NewGuid(), PlateNumber = definition.Plate, CreatedAt = now };
                dbContext.VehicleRecords.Add(vehicle);
            }
            vehicle.WarehouseId = warehouse.Id;
            vehicle.VehicleType = definition.Type;
            vehicle.Capacity = definition.Capacity;
            vehicle.IsActive = true;
            vehicle.UpdatedAt = now;
        }
    }

    private async Task SeedPromotionDataAsync(
        IReadOnlyDictionary<string, Product> products,
        IReadOnlyDictionary<string, ProductCategory> categories,
        Retailer retailer,
        Guid adminUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var campaignDefinitions = new[]
        {
            new
            {
                Code = "KM-001", Name = "Mừng khai trương - giảm giá sữa tươi",
                MinimumQuantity = 10m, DiscountPercent = 8m,
                StartAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime(),
                EndAt = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.FromHours(7)).ToUniversalTime(),
                ProductId = (Guid?)products["SKU001"].Id, CategoryId = (Guid?)null,
                RetailerId = (Guid?)null
            },
            new
            {
                Code = "KM-002", Name = "Ưu đãi ngành hàng sữa cho khách hàng thân thiết",
                MinimumQuantity = 5m, DiscountPercent = 6m,
                StartAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime(),
                EndAt = new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.FromHours(7)).ToUniversalTime(),
                ProductId = (Guid?)null, CategoryId = (Guid?)categories["CAT-02"].Id,
                RetailerId = (Guid?)retailer.Id
            }
        };

        foreach (var definition in campaignDefinitions)
        {
            var promotion = await dbContext.PromotionRecords.SingleOrDefaultAsync(
                item => item.Code == definition.Code, cancellationToken);
            if (promotion is null)
            {
                promotion = new Promotion
                {
                    Id = Guid.NewGuid(),
                    Code = definition.Code,
                    CreatedByUserId = adminUserId,
                    CreatedAt = now
                };
                dbContext.PromotionRecords.Add(promotion);
            }
            promotion.Name = definition.Name;
            promotion.PromotionType = PromotionTypes.Campaign;
            promotion.MinimumQuantity = definition.MinimumQuantity;
            promotion.DiscountPercent = definition.DiscountPercent;
            promotion.StartAt = definition.StartAt;
            promotion.EndAt = definition.EndAt;
            promotion.IsActive = true;
            promotion.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (definition.ProductId.HasValue && !await dbContext.PromotionProductRecords.AnyAsync(
                    item => item.PromotionId == promotion.Id && item.ProductId == definition.ProductId.Value,
                    cancellationToken))
                dbContext.PromotionProductRecords.Add(new PromotionProduct
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promotion.Id,
                    ProductId = definition.ProductId.Value
                });
            if (definition.CategoryId.HasValue && !await dbContext.PromotionCategoryRecords.AnyAsync(
                    item => item.PromotionId == promotion.Id && item.ProductCategoryId == definition.CategoryId.Value,
                    cancellationToken))
                dbContext.PromotionCategoryRecords.Add(new PromotionCategory
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promotion.Id,
                    ProductCategoryId = definition.CategoryId.Value
                });
            if (definition.RetailerId.HasValue && !await dbContext.PromotionRetailerRecords.AnyAsync(
                    item => item.PromotionId == promotion.Id && item.RetailerId == definition.RetailerId.Value,
                    cancellationToken))
                dbContext.PromotionRetailerRecords.Add(new PromotionRetailer
                {
                    Id = Guid.NewGuid(),
                    PromotionId = promotion.Id,
                    RetailerId = definition.RetailerId.Value
                });
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var batch = await dbContext.BatchRecords.SingleAsync(
            item => item.BatchCode == "WH-TD-SKU002-28102026", cancellationToken);
        var batchPromotion = await dbContext.PromotionRecords.SingleOrDefaultAsync(
            item => item.Code == "KM-003", cancellationToken);
        if (batchPromotion is null)
        {
            batchPromotion = new Promotion
            {
                Id = Guid.NewGuid(),
                Code = "KM-003",
                CreatedByUserId = adminUserId,
                CreatedAt = now
            };
            dbContext.PromotionRecords.Add(batchPromotion);
        }
        batchPromotion.Name = "Chiết khấu lô sữa chua gần hạn";
        batchPromotion.PromotionType = PromotionTypes.BatchDiscount;
        batchPromotion.MinimumQuantity = 1m;
        batchPromotion.DiscountPercent = 15m;
        batchPromotion.StartAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        batchPromotion.EndAt = new DateTimeOffset(2026, 10, 20, 23, 59, 59, TimeSpan.FromHours(7)).ToUniversalTime();
        batchPromotion.IsActive = true;
        batchPromotion.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var assignment = await dbContext.BatchPromotionRecords.SingleOrDefaultAsync(
            item => item.BatchId == batch.Id && item.PromotionId == batchPromotion.Id,
            cancellationToken);
        if (assignment is null)
        {
            assignment = new BatchPromotion
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                PromotionId = batchPromotion.Id,
                AssignedByUserId = adminUserId,
                AssignedAt = now
            };
            dbContext.BatchPromotionRecords.Add(assignment);
        }
        assignment.IsActive = true;
        assignment.RemovedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedQrLabelDataAsync(Guid warehouseKeeperId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new QrLabelSeed("TEM-000204", "WH-HCM-01-SKU001-30042027", 300m, null, QrLabelStatuses.Available, "Tem gốc cho lô sữa tiệt trùng"),
            new QrLabelSeed("TEM-000205", "WH-HCM-01-SKU001-30042027", 100m, "TEM-000204", QrLabelStatuses.Available, "Tem con tách từ tem TEM-000204"),
            new QrLabelSeed("TEM-000206", "WH-TD-SKU002-28102026", 80m, null, QrLabelStatuses.Available, "Tem cho lô sữa chua gần hạn"),
            new QrLabelSeed("TEM-000207", "WH-Q7-SKU003-10102026", 20m, null, QrLabelStatuses.Invalidated, "Tem lô thực phẩm hết hạn đã vô hiệu hóa"),
            new QrLabelSeed("TEM-000208", "WH-TD-SKU001-31032027", 200m, null, QrLabelStatuses.Available, "Tem gốc cho lô sữa tươi phục vụ soạn đơn tại kho Thủ Đức")
        };
        var labels = new Dictionary<string, BatchQrLabel>(StringComparer.Ordinal);
        foreach (var definition in definitions.Where(item => item.ParentQrCode is null))
            labels[definition.QrCode] = await UpsertQrLabelAsync(definition, null, null, now, cancellationToken);
        foreach (var definition in definitions.Where(item => item.ParentQrCode is not null))
        {
            var parent = labels[definition.ParentQrCode!];
            labels[definition.QrCode] = await UpsertQrLabelAsync(definition, parent.Id, parent.RootLabelId ?? parent.Id, now, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var definition in definitions)
        {
            var label = labels[definition.QrCode];
            var eventType = definition.ParentQrCode is not null ? QrLabelEventTypes.Split : QrLabelEventTypes.Generated;
            var operationKey = $"du-lieu-mau-{definition.QrCode.ToLowerInvariant()}-{eventType.ToLowerInvariant()}";
            var labelEvent = await dbContext.QrLabelEventRecords.SingleOrDefaultAsync(
                item => item.OperationKey == operationKey, cancellationToken);
            if (labelEvent is null)
            {
                labelEvent = new QrLabelEvent
                {
                    Id = Guid.NewGuid(),
                    OperationKey = operationKey,
                    BatchQrLabelId = label.Id,
                    WarehouseId = label.CurrentWarehouseId,
                    PerformedByUserId = warehouseKeeperId,
                    EventType = eventType,
                    FromStatus = definition.ParentQrCode is null ? string.Empty : QrLabelStatuses.Available,
                    ToStatus = QrLabelStatuses.Available,
                    Quantity = definition.ParentQrCode is null
                        ? labels.Values.Where(item => item.RootLabelId == label.Id).Sum(item => item.Quantity)
                        : label.Quantity,
                    ReferenceCode = definition.ParentQrCode,
                    Note = definition.Note,
                    OccurredAt = now.AddDays(-20)
                };
                dbContext.QrLabelEventRecords.Add(labelEvent);
            }
            if (definition.Status == QrLabelStatuses.Invalidated)
            {
                var invalidationKey = $"du-lieu-mau-{definition.QrCode.ToLowerInvariant()}-vo-hieu-hoa";
                var invalidation = await dbContext.QrLabelEventRecords.SingleOrDefaultAsync(
                    item => item.OperationKey == invalidationKey, cancellationToken);
                if (invalidation is null)
                {
                    invalidation = new QrLabelEvent
                    {
                        Id = Guid.NewGuid(),
                        OperationKey = invalidationKey,
                        BatchQrLabelId = label.Id,
                        WarehouseId = label.CurrentWarehouseId,
                        PerformedByUserId = warehouseKeeperId,
                        EventType = QrLabelEventTypes.Invalidated,
                        FromStatus = QrLabelStatuses.Available,
                        ToStatus = QrLabelStatuses.Invalidated,
                        Quantity = label.Quantity,
                        Note = "Vô hiệu hóa tem vì lô hàng đã hết hạn",
                        OccurredAt = now.AddDays(-1)
                    };
                    dbContext.QrLabelEventRecords.Add(invalidation);
                }
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<BatchQrLabel> UpsertQrLabelAsync(
        QrLabelSeed definition, Guid? parentId, Guid? rootId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var batch = await dbContext.BatchRecords.SingleAsync(item => item.BatchCode == definition.BatchCode, cancellationToken);
        var label = await dbContext.BatchQrLabelRecords.SingleOrDefaultAsync(item => item.QrCode == definition.QrCode, cancellationToken);
        var isNewLabel = label is null;
        if (label is null)
        {
            var id = Guid.NewGuid();
            label = new BatchQrLabel { Id = id, QrCode = definition.QrCode, GeneratedAt = now.AddDays(-20) };
            dbContext.BatchQrLabelRecords.Add(label);
        }
        label.BatchId = batch.Id;
        label.CurrentWarehouseId = batch.WarehouseId;
        label.ParentLabelId = parentId;
        label.RootLabelId = rootId ?? label.Id;
        if (isNewLabel)
        {
            label.Quantity = definition.Quantity;
            label.Status = definition.Status;
            label.Version = 1;
            label.UpdatedAt = now;
            label.InvalidatedAt = definition.Status == QrLabelStatuses.Invalidated ? now.AddDays(-1) : null;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return label;
    }

    private async Task<Dictionary<string, ProductCategory>> SeedCategoriesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new CategorySeed("CAT-01", "Đồ uống", "Đồ uống có hạn sử dụng ngắn", (string?)null),
            new CategorySeed("CAT-02", "Sữa và sản phẩm từ sữa", "Sữa và các sản phẩm sữa lên men", "CAT-01"),
            new CategorySeed("CAT-03", "Thực phẩm tươi", "Thực phẩm tươi được đóng gói", (string?)null),
            new CategorySeed("CAT-04", "Hàng theo mùa", "Danh mục ngừng hoạt động dùng để kiểm thử", (string?)null, false)
        };
        var categories = new Dictionary<string, ProductCategory>(StringComparer.Ordinal);

        foreach (var definition in definitions.Where(item => item.ParentCode is null))
        {
            categories[definition.Code] = await UpsertCategoryAsync(definition, null, now, cancellationToken);
        }

        foreach (var definition in definitions.Where(item => item.ParentCode is not null))
        {
            categories[definition.Code] = await UpsertCategoryAsync(
                definition,
                categories[definition.ParentCode!].Id,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return categories;
    }

    private async Task<ProductCategory> UpsertCategoryAsync(
        CategorySeed definition,
        Guid? parentCategoryId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategoryRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == definition.Code,
            cancellationToken);
        if (category is null)
        {
            category = new ProductCategory
            {
                Id = Guid.NewGuid(),
                Code = definition.Code,
                CreatedAt = now
            };
            dbContext.ProductCategoryRecords.Add(category);
        }

        category.ParentCategoryId = parentCategoryId;
        category.Name = definition.Name;
        category.Description = definition.Description;
        category.IsActive = definition.IsActive;
        category.UpdatedAt = now;
        return category;
    }

    private async Task<Dictionary<string, Product>> SeedProductsAsync(
        IReadOnlyDictionary<string, ProductCategory> categories,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new ProductSeed("SKU001", "Sữa tươi 1 lít", "Thùng", 168, 24, "CAT-02", ProductStatuses.Published),
            new ProductSeed("SKU002", "Sữa chua uống 180 ml", "Thùng", 336, 48, "CAT-02", ProductStatuses.Published),
            new ProductSeed("SKU003", "Bánh mì kẹp tươi", "Khay", 72, 12, "CAT-03", ProductStatuses.Draft),
            new ProductSeed("SKU004", "Nước cam 250 ml - thùng 48 hộp", "Thùng", 720, 120, "CAT-01", ProductStatuses.Inactive),
            new ProductSeed("SKU005", "Nước cam 250 ml - thùng 36 hộp", "Thùng", 720, 120, "CAT-01", ProductStatuses.Published, "SKU004")
        };
        var products = new Dictionary<string, Product>(StringComparer.Ordinal);

        foreach (var definition in definitions.Where(item => item.ReplacesSku is null))
        {
            products[definition.Sku] = await UpsertProductAsync(
                definition,
                categories[definition.CategoryCode].Id,
                null,
                now,
                cancellationToken);
        }

        foreach (var definition in definitions.Where(item => item.ReplacesSku is not null))
        {
            products[definition.Sku] = await UpsertProductAsync(
                definition,
                categories[definition.CategoryCode].Id,
                products[definition.ReplacesSku!].Id,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedPriceHistoryAsync(products["SKU001"], [235_000m, 245_000m, 252_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU002"], [310_000m, 320_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU003"], [180_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU004"], [400_000m, 415_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU005"], [325_000m], now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return products;
    }

    private async Task<Product> UpsertProductAsync(
        ProductSeed definition,
        Guid categoryId,
        Guid? replacesProductId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.ProductRecords.SingleOrDefaultAsync(
            candidate => candidate.Sku == definition.Sku,
            cancellationToken);
        if (product is null)
        {
            product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = definition.Sku,
                CreatedAt = now.AddDays(-90)
            };
            dbContext.ProductRecords.Add(product);
        }

        product.ProductCategoryId = categoryId;
        product.ReplacesProductId = replacesProductId;
        product.Name = definition.Name;
        product.Unit = definition.Unit;
        product.ShelfLifeHours = definition.ShelfLifeHours;
        product.NearDateThresholdHours = definition.NearDateThresholdHours;
        product.Status = definition.Status;
        product.PublishedAt = definition.Status is ProductStatuses.Published or ProductStatuses.Inactive
            ? now.AddDays(-60)
            : null;
        product.InactivatedAt = definition.Status == ProductStatuses.Inactive ? now.AddDays(-15) : null;
        product.UpdatedAt = now;
        return product;
    }

    private async Task SeedPriceHistoryAsync(
        Product product,
        IReadOnlyList<decimal> prices,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ProductPriceRecords.AnyAsync(
                candidate => candidate.ProductId == product.Id,
                cancellationToken))
        {
            return;
        }

        for (var index = 0; index < prices.Count; index++)
        {
            var effectiveFrom = now.AddDays(-30 * (prices.Count - index));
            var isLast = index == prices.Count - 1;
            var isActive = isLast && product.Status != ProductStatuses.Inactive;
            dbContext.ProductPriceRecords.Add(new ProductPrice
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                UnitPrice = prices[index],
                EffectiveFrom = effectiveFrom,
                EffectiveTo = isActive
                    ? null
                    : isLast
                        ? product.InactivatedAt ?? now
                        : now.AddDays(-30 * (prices.Count - index - 1)),
                IsActive = isActive,
                CreatedAt = effectiveFrom
            });
        }
    }

    private async Task<Dictionary<string, UserAccount>> SeedUsersAsync(
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usersByRole = new Dictionary<string, UserAccount>(StringComparer.Ordinal);
        foreach (var definition in DemoUsers)
        {
            var user = await dbContext.UserAccountRecords.SingleOrDefaultAsync(
                candidate => candidate.Email == definition.Email,
                cancellationToken);
            if (user is null)
            {
                var userId = Guid.NewGuid();
                user = new UserAccount
                {
                    Id = userId,
                    IdentityProvider = IdentityProviders.Local,
                    Email = definition.Email,
                    CreatedAt = now
                };
                dbContext.UserAccountRecords.Add(user);
            }

            user.FullName = definition.FullName;
            user.Role = definition.Role;
            user.IdentityProvider = IdentityProviders.Local;
            user.IsActive = true;
            user.UpdatedAt = now;
            usersByRole.Add(definition.Role, user);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var user in usersByRole.Values)
        {
            var credential = await dbContext.UserCredentials.SingleOrDefaultAsync(
                candidate => candidate.UserAccountId == user.Id,
                cancellationToken);
            if (credential is null)
            {
                credential = new UserCredential
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = user.Id,
                    CreatedAt = now
                };
                dbContext.UserCredentials.Add(credential);
            }

            credential.PasswordHash = passwordHasher.Hash(password);
            credential.FailedLoginCount = 0;
            credential.LockoutEndAt = null;
            credential.MustChangePassword = false;
            credential.PasswordChangedAt = now;
            credential.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return usersByRole;
    }

    private async Task<IReadOnlyDictionary<string, Region>> SeedRegionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new RegionSeed("HCM", "Thành phố Hồ Chí Minh", "Khu vực bán hàng Thành phố Hồ Chí Minh", true),
            new RegionSeed("BD", "Bình Dương", "Khu vực bán hàng tỉnh Bình Dương", true),
            new RegionSeed("LA", "Long An", "Khu vực tạm ngừng hoạt động dùng để kiểm thử", false)
        };
        var regions = new Dictionary<string, Region>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var region = definition.Code == DemoRegionCode
                ? await dbContext.RegionRecords.SingleOrDefaultAsync(
                    candidate => candidate.Code == definition.Code || candidate.Code == "REG-HCM", cancellationToken)
                : await dbContext.RegionRecords.SingleOrDefaultAsync(
                    candidate => candidate.Code == definition.Code, cancellationToken);
            if (region is null)
            {
                region = new Region { Id = Guid.NewGuid(), CreatedAt = now };
                dbContext.RegionRecords.Add(region);
            }

            region.Code = definition.Code;
            region.Name = definition.Name;
            region.Description = definition.Description;
            region.IsActive = definition.IsActive;
            region.UpdatedAt = now;
            regions[definition.Code] = region;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return regions;
    }

    private async Task<Warehouse> SeedWarehouseAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var warehouse = await dbContext.WarehouseRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoWarehouseCode,
            cancellationToken);
        if (warehouse is null)
        {
            warehouse = new Warehouse { Id = Guid.NewGuid(), Code = DemoWarehouseCode, CreatedAt = now };
            dbContext.WarehouseRecords.Add(warehouse);
        }

        warehouse.Name = "Kho trung tâm Củ Chi";
        warehouse.Type = WarehouseTypes.Central;
        warehouse.Address = "Đường Nguyễn Thị Rành, huyện Củ Chi, Thành phố Hồ Chí Minh";
        warehouse.MinimumStockThresholdCartons = 150;
        warehouse.IsActive = true;
        warehouse.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return warehouse;
    }

    private async Task<IReadOnlyCollection<Warehouse>> SeedWarehouseConfigurationDataAsync(
        Warehouse centralWarehouse,
        Retailer retailer,
        IReadOnlyDictionary<string, Product> products,
        Guid adminUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new WarehouseSeed(centralWarehouse.Code, "Kho trung tâm Củ Chi", WarehouseTypes.Central,
                "Đường Nguyễn Thị Rành, huyện Củ Chi, Thành phố Hồ Chí Minh", 10.9747000m, 106.4931000m, 150),
            new WarehouseSeed("WH-TD", "Kho vùng Thủ Đức", WarehouseTypes.Regional,
                "Đường Võ Chí Công, thành phố Thủ Đức, Thành phố Hồ Chí Minh", 10.8480000m, 106.7720000m, 100),
            new WarehouseSeed("WH-Q7", "Kho vùng Quận 7", WarehouseTypes.Regional,
                "Đường Nguyễn Văn Linh, Quận 7, Thành phố Hồ Chí Minh", 10.7298000m, 106.7219000m, 80)
        };
        var warehouses = new List<Warehouse>();
        foreach (var definition in definitions)
        {
            var warehouse = await dbContext.WarehouseRecords.SingleOrDefaultAsync(
                item => item.Code == definition.Code, cancellationToken);
            if (warehouse is null)
            {
                warehouse = new Warehouse { Id = Guid.NewGuid(), Code = definition.Code, CreatedAt = now };
                dbContext.WarehouseRecords.Add(warehouse);
            }

            warehouse.Name = definition.Name;
            warehouse.Type = definition.Type;
            warehouse.Address = definition.Address;
            warehouse.MinimumStockThresholdCartons = definition.MinimumStockThresholdCartons;
            warehouse.IsActive = true;
            warehouse.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            var location = await dbContext.MapLocationRecords.SingleOrDefaultAsync(
                item => item.WarehouseId == warehouse.Id, cancellationToken);
            if (location is null)
            {
                location = new MapLocation { Id = Guid.NewGuid(), WarehouseId = warehouse.Id, CreatedAt = now };
                dbContext.MapLocationRecords.Add(location);
            }

            location.Latitude = definition.Latitude;
            location.Longitude = definition.Longitude;
            location.FormattedAddress = definition.Address;
            location.ProviderPlaceId = $"du-lieu-mau-{definition.Code.ToLowerInvariant()}";
            location.Provider = "Manual";
            location.Version = Math.Max(1, location.Version);
            location.NormalizedAt = null;
            location.UpdatedAt = now;
            warehouses.Add(warehouse);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var warehouse in warehouses)
        {
            var activeTransitRules = await dbContext.TransitTimeRuleRecords.Where(
                item => item.WarehouseId == warehouse.Id && item.IsActive).ToArrayAsync(cancellationToken);
            if (activeTransitRules.Length == 0)
            {
                dbContext.TransitTimeRuleRecords.AddRange(
                    CreateTransitRule(warehouse.Id, 0, 10_000, 60, 1_440, 120_000m, now),
                    CreateTransitRule(warehouse.Id, 10_001, 20_000, 60, 2_880, 220_000m, now));
            }
            else
            {
                foreach (var rule in activeTransitRules)
                    rule.EstimatedTransportCost = rule.MaxDistanceMeters <= 10_000 ? 120_000m : 220_000m;
            }
        }

        // The shared transit table the ETA function reads. Without it a fresh database answers
        // every ETA request with "no transit table has been published yet".
        if (!await dbContext.TransitTableRecords.AnyAsync(item => item.IsActive, cancellationToken))
        {
            var transitTable = new TransitTable
            {
                Id = Guid.NewGuid(),
                Version = 1,
                MaxServingDistanceMeters = 20_000,
                EffectiveFrom = now,
                IsActive = true,
                Note = "Bảng vận chuyển dùng chung mặc định cho môi trường phát triển",
                CreatedByUserId = adminUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.TransitTableRecords.Add(transitTable);
            dbContext.TransitTableBandRecords.AddRange(
                CreateTransitBand(transitTable.Id, 0, 10_000, 60, 1_440, 80_000m, now),
                CreateTransitBand(transitTable.Id, 10_000, 20_000, 60, 2_880, 120_000m, now));
        }

        var categoryId = products["SKU001"].ProductCategoryId;
        if (!await dbContext.RetailerProductMlorRecords.AnyAsync(
                item => item.RetailerId == retailer.Id && item.ProductCategoryId == categoryId && item.IsActive,
                cancellationToken))
        {
            dbContext.RetailerProductMlorRecords.Add(new RetailerProductMlor
            {
                Id = Guid.NewGuid(),
                RetailerId = retailer.Id,
                ProductCategoryId = categoryId,
                MinimumLifeRatio = 0.25m,
                Version = 1,
                UpdatedByUserId = adminUserId,
                EffectiveFrom = now,
                IsActive = true
            });
        }

        foreach (var definition in new[] { (Product: products["SKU002"], Ratio: 0.30m) })
        {
            if (!await dbContext.RetailerProductMlorRecords.AnyAsync(
                    item => item.RetailerId == retailer.Id && item.ProductId == definition.Product.Id && item.IsActive,
                    cancellationToken))
            {
                dbContext.RetailerProductMlorRecords.Add(new RetailerProductMlor
                {
                    Id = Guid.NewGuid(),
                    RetailerId = retailer.Id,
                    ProductId = definition.Product.Id,
                    MinimumLifeRatio = definition.Ratio,
                    MinimumLifeHours = (int)Math.Ceiling(definition.Product.ShelfLifeHours * definition.Ratio),
                    Version = 1,
                    UpdatedByUserId = adminUserId,
                    EffectiveFrom = now,
                    IsActive = true
                });
            }
        }

        var configurations = new[]
        {
            new ConfigurationSeed(BusinessConfigurationKeys.AllocationWeights,
                "{\"expiry\":0.5,\"distance\":0.3,\"coverage\":0.2}", "object",
                "Trọng số phân bổ ưu tiên hạn sử dụng, khoảng cách và độ phủ"),
            new ConfigurationSeed(BusinessConfigurationKeys.ServiceRadiusMeters, "20000", "integer",
                "Bán kính phục vụ tối đa mặc định là 20 kilômét"),
            new ConfigurationSeed(BusinessConfigurationKeys.DefaultMlorRatio, "0.25", "decimal",
                "Tỷ lệ hạn sử dụng còn lại tối thiểu mặc định là 25 phần trăm"),
            new ConfigurationSeed(BusinessConfigurationKeys.OrderBufferStockCartons, "0", "integer",
                "Tồn kho đệm mặc định dành cho giới hạn số lượng đặt hàng"),
            new ConfigurationSeed(BusinessConfigurationKeys.OrderStepCartons, "1", "integer",
                "Bước số lượng đặt hàng mặc định là một thùng"),
            new ConfigurationSeed(BusinessConfigurationKeys.EtaSchedule,
                "{\"timeZone\":\"Asia/Ho_Chi_Minh\",\"salesSubmissionAdviceLocalTime\":\"11:00\"," +
                "\"orderReviewReminderLocalTime\":\"11:30\",\"allocationRunLocalTime\":\"00:00\"," +
                "\"planApprovalReminderLocalTime\":\"13:30\"," +
                "\"planApprovalDeadlineLocalTime\":\"14:00\",\"preparationMinutes\":240," +
                "\"bufferMinutes\":60,\"dispatchSlotsLocalTime\":[\"06:00\",\"14:00\"]}", "object",
                "Lịch ngày chạy đợt: chốt đợt lúc 00:00 (đóng ngày vừa kết thúc); trong ngày đó khuyến nghị " +
                "gửi đơn trước 11:00, nhắc duyệt đơn 11:30; sau khi chốt thì " +
                "nhắc duyệt phương án 13:30, hạn duyệt 14:00, soạn 4 giờ, đệm 1 giờ, xuất 06:00 và 14:00")
        };
        foreach (var definition in configurations)
        {
            if (!await dbContext.SystemConfigurationRecords.AnyAsync(
                    item => item.ConfigKey == definition.Key && item.IsActive, cancellationToken))
            {
                dbContext.SystemConfigurationRecords.Add(new SystemConfiguration
                {
                    Id = Guid.NewGuid(),
                    ConfigKey = definition.Key,
                    ConfigValue = definition.Value,
                    ValueType = definition.ValueType,
                    Description = definition.Description,
                    Version = 1,
                    IsActive = true,
                    UpdatedByUserId = adminUserId,
                    EffectiveFrom = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return warehouses;
    }

    private static TransitTimeRule CreateTransitRule(
        Guid warehouseId, int minimumDistance, int maximumDistance,
        int handlingMinutes, int transitMinutes, decimal estimatedTransportCost, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            MinDistanceMeters = minimumDistance,
            MaxDistanceMeters = maximumDistance,
            HandlingMinutes = handlingMinutes,
            TransitMinutes = transitMinutes,
            EstimatedTransportCost = estimatedTransportCost,
            MaxServingDistanceMeters = 20_000,
            Version = 1,
            EffectiveFrom = now,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static TransitTableBand CreateTransitBand(
        Guid transitTableId, int minimumDistance, int maximumDistance,
        int handlingMinutes, int transitMinutes, decimal estimatedCost, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            TransitTableId = transitTableId,
            MinDistanceMeters = minimumDistance,
            MaxDistanceMeters = maximumDistance,
            HandlingMinutes = handlingMinutes,
            TransitMinutes = transitMinutes,
            EstimatedCostAmount = estimatedCost,
            CreatedAt = now
        };

    private async Task<IReadOnlyDictionary<string, SalesGroup>> SeedSalesGroupsAsync(
        IReadOnlyDictionary<string, Region> regions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new SalesGroupSeed("HCM-A", "Nhóm bán hàng Hồ Chí Minh A", "HCM", true),
            new SalesGroupSeed("HCM-B", "Nhóm bán hàng Hồ Chí Minh B", "HCM", true),
            new SalesGroupSeed("BD-A", "Nhóm bán hàng Bình Dương A", "BD", true),
            new SalesGroupSeed("LA-A", "Nhóm bán hàng Long An A", "LA", false)
        };
        var groups = new Dictionary<string, SalesGroup>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var group = definition.Code == DemoSalesGroupCode
                ? await dbContext.SalesGroupRecords.SingleOrDefaultAsync(
                    candidate => candidate.Code == definition.Code || candidate.Code == "SG-HCM-01", cancellationToken)
                : await dbContext.SalesGroupRecords.SingleOrDefaultAsync(
                    candidate => candidate.Code == definition.Code, cancellationToken);
            if (group is null)
            {
                group = new SalesGroup { Id = Guid.NewGuid(), CreatedAt = now };
                dbContext.SalesGroupRecords.Add(group);
            }

            group.RegionId = regions[definition.RegionCode].Id;
            group.Code = definition.Code;
            group.Name = definition.Name;
            group.IsActive = definition.IsActive;
            group.UpdatedAt = now;
            groups[definition.Code] = group;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return groups;
    }

    private async Task<Retailer> SeedRetailerAsync(
        Guid regionId,
        Guid declaredByUserId,
        Guid reviewedByUserId,
        Guid retailerUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var retailer = await dbContext.RetailerRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoRetailerCode || candidate.Code == LegacyDemoRetailerCode,
            cancellationToken);
        if (retailer is null)
        {
            retailer = new Retailer { Id = Guid.NewGuid(), Code = DemoRetailerCode, CreatedAt = now };
            dbContext.RetailerRecords.Add(retailer);
        }

        retailer.RegionId = regionId;
        retailer.UserAccountId = retailerUserId;
        retailer.Code = DemoRetailerCode;
        retailer.Name = "Cửa hàng An Phú";
        retailer.ContactName = "Nguyễn Văn Minh";
        retailer.Phone = "+84 28 0000 0001";
        retailer.Email = "cuahang.anphu@fscm.local";
        retailer.Address = "Phường An Phú, thành phố Thủ Đức, Thành phố Hồ Chí Minh";
        retailer.Status = RetailerStatuses.Active;
        retailer.RetailerType = RetailerTypes.GroceryStore;
        retailer.ForecastRevenue = 72_000_000;
        retailer.StoreSizeSquareMeters = 48;
        retailer.StoreSizeCategory = "M";
        retailer.PurchasingCapacity = 135;
        retailer.PurchaseFrequencyPerMonth = 8;
        retailer.CreditTermDays = 14;
        retailer.RequestedCreditTermDays = 14;
        retailer.PotentialStatus = "A";
        retailer.AssessmentNote = "Điểm bán có doanh thu ổn định và tần suất nhập hàng cao.";
        retailer.DeclaredByUserId = declaredByUserId;
        retailer.AssessedByUserId = declaredByUserId;
        retailer.AssessedAt = now.AddDays(-31);
        retailer.ReviewedByUserId = reviewedByUserId;
        retailer.ReviewedAt = now.AddDays(-30);
        retailer.ReviewReason = "Thông tin hợp lệ, đủ điều kiện kích hoạt.";
        retailer.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var retailerLocation = await dbContext.MapLocationRecords.SingleOrDefaultAsync(
            item => item.RetailerId == retailer.Id, cancellationToken);
        if (retailerLocation is null)
        {
            retailerLocation = new MapLocation
            {
                Id = Guid.NewGuid(),
                RetailerId = retailer.Id,
                CreatedAt = now
            };
            dbContext.MapLocationRecords.Add(retailerLocation);
        }
        retailerLocation.Latitude = 10.8020000m;
        retailerLocation.Longitude = 106.7430000m;
        retailerLocation.FormattedAddress = retailer.Address;
        retailerLocation.ProviderPlaceId = "du-lieu-mau-cua-hang-an-phu";
        retailerLocation.Provider = "Manual";
        retailerLocation.Version = Math.Max(1, retailerLocation.Version);
        retailerLocation.NormalizedAt = null;
        retailerLocation.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return retailer;
    }

    private async Task SeedAdditionalRetailersAsync(
        IReadOnlyDictionary<string, Region> regions,
        IReadOnlyDictionary<string, SalesGroup> salesGroups,
        IReadOnlyDictionary<string, UserAccount> users,
        Guid defaultMlorCategoryId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new RetailerSeed("10004128", "Siêu thị mini Bình Minh", "Trần Thị Mai", "0909000102", "Phường Bến Nghé, Quận 1, Thành phố Hồ Chí Minh", "HCM", "HCM-A", RetailerStatuses.Active, RetailerTypes.MiniSupermarket, 55_000_000m, 72m, 125m, 10, 30, "A", "Mặt bằng lớn, năng lực nhập hàng thuộc nhóm A."),
            new RetailerSeed("KB-0012", "Cửa hàng Tân Lập", "Lê Quốc Bảo", "0909000103", "Phường Tân Hiệp, Thành phố Tân Uyên, Bình Dương", "BD", "BD-A", RetailerStatuses.PendingApproval, RetailerTypes.ConvenienceStore, 38_000_000m, 42m, 75m, 6, 7, "B", "Hồ sơ đang chờ quản trị viên phê duyệt."),
            new RetailerSeed("KB-0013", "Sạp Minh Châu", "Phạm Ngọc Châu", "0909000104", "Chợ Tân An, Thành phố Tân An, Long An", "LA", "LA-A", RetailerStatuses.Rejected, RetailerTypes.MarketStall, 18_000_000m, 22m, 35m, 4, 0, "C", "Cần bổ sung giấy tờ xác minh địa chỉ kinh doanh."),
            new RetailerSeed("10004129", "Tạp hóa Hòa Phát", "Võ Thanh Hòa", "0909000105", "Phường Hiệp Bình Chánh, thành phố Thủ Đức, Thành phố Hồ Chí Minh", "HCM", "HCM-B", RetailerStatuses.Inactive, RetailerTypes.GroceryStore, 32_000_000m, 28m, 65m, 5, 7, "B", "Điểm bán tạm ngừng hoạt động theo đề nghị của chủ cửa hàng.")
        };

        foreach (var definition in definitions)
        {
            var retailer = await dbContext.RetailerRecords.SingleOrDefaultAsync(item => item.Code == definition.Code, cancellationToken);
            if (retailer is null)
            {
                retailer = new Retailer { Id = Guid.NewGuid(), Code = definition.Code, CreatedAt = now.AddDays(-20) };
                dbContext.RetailerRecords.Add(retailer);
            }
            retailer.RegionId = regions[definition.RegionCode].Id;
            retailer.Name = definition.Name;
            retailer.ContactName = definition.ContactName;
            retailer.Phone = definition.Phone;
            retailer.Email = null;
            retailer.Address = definition.Address;
            retailer.Status = definition.Status;
            retailer.RetailerType = definition.RetailerType;
            retailer.ForecastRevenue = definition.ForecastRevenue;
            retailer.StoreSizeSquareMeters = definition.StoreSize;
            retailer.StoreSizeCategory = definition.StoreSize < 30 ? "S" : definition.StoreSize <= 60 ? "M" : "L";
            retailer.PurchasingCapacity = definition.Capacity;
            retailer.PurchaseFrequencyPerMonth = definition.Frequency;
            retailer.RequestedCreditTermDays = definition.CreditTermDays;
            retailer.CreditTermDays = definition.Status is RetailerStatuses.Active or RetailerStatuses.Inactive
                ? definition.CreditTermDays : 0;
            retailer.PotentialStatus = definition.PotentialClass;
            retailer.AssessmentNote = definition.Note;
            retailer.DeclaredByUserId = users[SystemRoles.Sales].Id;
            retailer.AssessedByUserId = users[SystemRoles.Sales].Id;
            retailer.AssessedAt = now.AddDays(-19);
            retailer.ReviewedByUserId = definition.Status == RetailerStatuses.PendingApproval ? null : users[SystemRoles.Admin].Id;
            retailer.ReviewedAt = definition.Status == RetailerStatuses.PendingApproval ? null : now.AddDays(-18);
            retailer.ReviewReason = definition.Status switch
            {
                RetailerStatuses.Rejected => "Từ chối do chưa đủ giấy tờ xác minh địa chỉ.",
                RetailerStatuses.Inactive => "Đã duyệt trước đây và hiện tạm ngừng hoạt động.",
                RetailerStatuses.Active => "Thông tin hợp lệ, đủ điều kiện kích hoạt.",
                _ => null
            };
            retailer.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            var groupId = salesGroups[definition.SalesGroupCode].Id;
            if (!await dbContext.RetailerSalesAssignmentRecords.AnyAsync(
                    item => item.RetailerId == retailer.Id && item.SalesGroupId == groupId && item.IsActive,
                    cancellationToken))
            {
                dbContext.RetailerSalesAssignmentRecords.Add(new RetailerSalesAssignment
                {
                    Id = Guid.NewGuid(),
                    RetailerId = retailer.Id,
                    SalesGroupId = groupId,
                    AssignedAt = now.AddDays(-20),
                    IsActive = true
                });
            }

            if (!await dbContext.RetailerProductMlorRecords.AnyAsync(
                    item => item.RetailerId == retailer.Id && item.IsActive, cancellationToken))
            {
                dbContext.RetailerProductMlorRecords.Add(new RetailerProductMlor
                {
                    Id = Guid.NewGuid(),
                    RetailerId = retailer.Id,
                    ProductCategoryId = defaultMlorCategoryId,
                    MinimumLifeRatio = 0.25m,
                    Version = 1,
                    UpdatedByUserId = users[SystemRoles.Sales].Id,
                    EffectiveFrom = now.AddDays(-20),
                    IsActive = true
                });
            }
        }
    }

    private async Task SeedWarehouseAssignmentsAsync(
        IReadOnlyDictionary<string, UserAccount> users,
        Guid warehouseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var role in new[] { SystemRoles.Operator, SystemRoles.WarehouseKeeper })
        {
            var userId = users[role].Id;
            var assignment = await dbContext.UserWarehouseAssignmentRecords.SingleOrDefaultAsync(
                candidate => candidate.UserAccountId == userId && candidate.WarehouseId == warehouseId,
                cancellationToken);
            if (assignment is null)
            {
                assignment = new UserWarehouseAssignment
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = userId,
                    WarehouseId = warehouseId,
                    AssignedAt = now
                };
                dbContext.UserWarehouseAssignmentRecords.Add(assignment);
            }

            assignment.IsActive = true;
        }
    }

    private async Task SeedInventoryDataAsync(
        IReadOnlyCollection<Warehouse> warehouses,
        IReadOnlyDictionary<string, Product> products,
        Guid warehouseKeeperId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var warehouseByCode = warehouses.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var definitions = new[]
        {
            new InventorySeed("PN-260901-01", "WH-HCM-01", "SKU001", new DateOnly(2027, 4, 30), 500m, 60m, "LÔ-SỮA-THÁNG-09", BatchExpiryStatuses.Valid),
            new InventorySeed("PN-261001-02", "WH-TD", "SKU001", new DateOnly(2027, 3, 31), 200m, 0m, "LÔ-SỮA-THỦ-ĐỨC-THÁNG-10", BatchExpiryStatuses.Valid),
            new InventorySeed("PN-261001-01", "WH-TD", "SKU002", new DateOnly(2026, 10, 28), 120m, 20m, "LÔ-SỮA-CHUA-THÁNG-10", BatchExpiryStatuses.NearExpiry),
            new InventorySeed("PN-260901-02", "WH-Q7", "SKU003", new DateOnly(2026, 10, 10), 30m, 0m, "LÔ-THỰC-PHẨM-THÁNG-09", BatchExpiryStatuses.Expired)
        };

        foreach (var definition in definitions)
        {
            var warehouse = warehouseByCode[definition.WarehouseCode];
            var product = products[definition.Sku];
            if (!await dbContext.UserWarehouseAssignmentRecords.AnyAsync(item =>
                    item.UserAccountId == warehouseKeeperId && item.WarehouseId == warehouse.Id && item.IsActive,
                    cancellationToken))
            {
                dbContext.UserWarehouseAssignmentRecords.Add(new UserWarehouseAssignment
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = warehouseKeeperId,
                    WarehouseId = warehouse.Id,
                    AssignedAt = now.AddDays(-60),
                    IsActive = true
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            var receivedAt = now.AddDays(-30);
            var receipt = await dbContext.GoodsReceiptRecords.SingleOrDefaultAsync(
                item => item.ReceiptCode == definition.ReceiptCode, cancellationToken);
            if (receipt is null)
            {
                receipt = new GoodsReceipt { Id = Guid.NewGuid(), ReceiptCode = definition.ReceiptCode, CreatedAt = receivedAt };
                dbContext.GoodsReceiptRecords.Add(receipt);
            }
            receipt.IdempotencyKey = $"du-lieu-mau-{definition.ReceiptCode.ToLowerInvariant()}";
            receipt.RequestHash = new string('0', 64);
            receipt.WarehouseId = warehouse.Id;
            receipt.CreatedByUserId = warehouseKeeperId;
            receipt.ApprovedByUserId = warehouseKeeperId;
            receipt.Status = GoodsReceiptStatuses.Received;
            receipt.ReceivedAt = receivedAt;
            receipt.ApprovedAt = receivedAt;
            receipt.Version = Math.Max(receipt.Version, 1);
            receipt.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            var line = await dbContext.GoodsReceiptLineRecords.SingleOrDefaultAsync(item =>
                item.GoodsReceiptId == receipt.Id && item.ProductId == product.Id &&
                item.ExpirationDate == definition.ExpirationDate, cancellationToken);
            if (line is null)
            {
                line = new GoodsReceiptLine { Id = Guid.NewGuid(), GoodsReceiptId = receipt.Id, ProductId = product.Id, CreatedAt = receivedAt };
                dbContext.GoodsReceiptLineRecords.Add(line);
            }
            line.SourceLotCode = definition.SourceLotCode;
            line.ReceivedQuantity = definition.OnHand;
            line.ProductionDate = definition.ExpirationDate.AddDays(
                -(int)Math.Ceiling(product.ShelfLifeHours / 24m));
            line.ExpirationDate = definition.ExpirationDate;
            await dbContext.SaveChangesAsync(cancellationToken);

            var batchCode = $"{warehouse.Code}-{product.Sku}-{definition.ExpirationDate:ddMMyyyy}";
            var batch = await dbContext.BatchRecords.SingleOrDefaultAsync(item => item.BatchCode == batchCode, cancellationToken);
            var isNewBatch = batch is null;
            if (batch is null)
            {
                var batchId = Guid.NewGuid();
                batch = new Batch { Id = batchId, RootBatchId = batchId, BatchCode = batchCode, CreatedAt = receivedAt };
                dbContext.BatchRecords.Add(batch);
            }
            batch.WarehouseId = warehouse.Id;
            batch.ProductId = product.Id;
            batch.GoodsReceiptLineId = line.Id;
            if (isNewBatch) batch.Quantity = definition.OnHand;
            batch.ExpirationDate = definition.ExpirationDate;
            batch.ExpiryStatus = definition.ExpiryStatus;
            batch.StockStatus = BatchStockStatuses.InStock;
            batch.Version = Math.Max(batch.Version, 1);
            batch.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            var balance = await dbContext.InventoryBalanceRecords.SingleOrDefaultAsync(item => item.BatchId == batch.Id, cancellationToken);
            if (balance is null)
            {
                balance = new InventoryBalance
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    PhysicalQuantity = definition.OnHand,
                    ReservedQuantity = definition.Reserved,
                    AvailableQuantity = definition.OnHand - definition.Reserved,
                    Version = 1,
                    UpdatedAt = now
                };
                dbContext.InventoryBalanceRecords.Add(balance);
            }
            else
            {
                balance.PhysicalQuantity = definition.OnHand;
                balance.ReservedQuantity = definition.Reserved;
                balance.AvailableQuantity = definition.OnHand - definition.Reserved;
                balance.Version = Math.Max(balance.Version, 1);
                balance.UpdatedAt = now;
            }

            if (!await dbContext.InventoryTransactionRecords.AnyAsync(item =>
                    item.BatchId == batch.Id && item.ReferenceCode == receipt.ReceiptCode &&
                    item.TransactionType == InventoryTransactionTypes.GoodsReceipt, cancellationToken))
            {
                dbContext.InventoryTransactionRecords.Add(new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    WarehouseId = warehouse.Id,
                    ProductId = product.Id,
                    GoodsReceiptId = receipt.Id,
                    GoodsReceiptLineId = line.Id,
                    CreatedByUserId = warehouseKeeperId,
                    TransactionType = InventoryTransactionTypes.GoodsReceipt,
                    SourceType = InventoryTransactionSourceTypes.GoodsReceipt,
                    SourceId = receipt.Id,
                    ReferenceCode = receipt.ReceiptCode,
                    PhysicalQuantityDelta = definition.OnHand,
                    PhysicalQuantityAfter = definition.OnHand,
                    ReservedQuantityAfter = definition.Reserved,
                    AvailableQuantityAfter = definition.OnHand - definition.Reserved,
                    Note = "Dữ liệu mẫu nhập hàng vào kho",
                    OccurredAt = receivedAt
                });
            }
        }
    }

    private async Task SeedSalesGroupMembershipsAsync(
        IReadOnlyDictionary<string, UserAccount> users,
        IReadOnlyDictionary<string, SalesGroup> salesGroups,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var primaryGroupId = salesGroups[DemoSalesGroupCode].Id;
        foreach (var role in new[] { SystemRoles.SalesManager, SystemRoles.Sales })
        {
            var userId = users[role].Id;
            var membership = await dbContext.SalesGroupMemberRecords.SingleOrDefaultAsync(
                candidate => candidate.UserAccountId == userId &&
                    candidate.SalesGroupId == primaryGroupId && candidate.IsActive,
                cancellationToken);
            if (membership is null)
            {
                membership = new SalesGroupMember
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = userId,
                    SalesGroupId = primaryGroupId,
                    AssignedAt = now
                };
                dbContext.SalesGroupMemberRecords.Add(membership);
            }

            membership.IsActive = true;
            membership.UnassignedAt = null;
        }

        var retailerUserId = users[SystemRoles.Retailer].Id;
        var invalidRetailerMemberships = await dbContext.SalesGroupMemberRecords
            .Where(candidate => candidate.UserAccountId == retailerUserId && candidate.IsActive)
            .ToArrayAsync(cancellationToken);
        foreach (var membership in invalidRetailerMemberships)
        {
            membership.IsActive = false;
            membership.UnassignedAt = now;
        }

        var salesUserId = users[SystemRoles.Sales].Id;
        var previousGroupId = salesGroups["HCM-B"].Id;
        if (!await dbContext.SalesGroupMemberRecords.AnyAsync(
                candidate => candidate.UserAccountId == salesUserId &&
                    candidate.SalesGroupId == previousGroupId && !candidate.IsActive,
                cancellationToken))
        {
            dbContext.SalesGroupMemberRecords.Add(new SalesGroupMember
            {
                Id = Guid.NewGuid(),
                UserAccountId = salesUserId,
                SalesGroupId = previousGroupId,
                AssignedAt = now.AddDays(-60),
                UnassignedAt = now.AddDays(-30),
                IsActive = false
            });
        }
    }

    private async Task SeedRetailerAssignmentsAsync(
        Guid retailerId,
        IReadOnlyDictionary<string, SalesGroup> salesGroups,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var salesGroupId = salesGroups[DemoSalesGroupCode].Id;
        var assignment = await dbContext.RetailerSalesAssignmentRecords.SingleOrDefaultAsync(
            candidate => candidate.RetailerId == retailerId &&
                candidate.SalesGroupId == salesGroupId && candidate.IsActive,
            cancellationToken);
        if (assignment is null)
        {
            assignment = new RetailerSalesAssignment
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                SalesGroupId = salesGroupId,
                AssignedAt = now
            };
            dbContext.RetailerSalesAssignmentRecords.Add(assignment);
        }

        assignment.IsActive = true;
        assignment.UnassignedAt = null;

        var previousGroupId = salesGroups["HCM-B"].Id;
        if (!await dbContext.RetailerSalesAssignmentRecords.AnyAsync(
                candidate => candidate.RetailerId == retailerId &&
                    candidate.SalesGroupId == previousGroupId && !candidate.IsActive,
                cancellationToken))
        {
            dbContext.RetailerSalesAssignmentRecords.Add(new RetailerSalesAssignment
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                SalesGroupId = previousGroupId,
                AssignedAt = now.AddDays(-90),
                UnassignedAt = now.AddDays(-60),
                IsActive = false
            });
        }
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < 12 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new InvalidOperationException(
                "SeedData:DefaultPassword must be at least 12 characters and contain uppercase, lowercase, numeric, and special characters.");
        }
    }

    private sealed record CategorySeed(
        string Code,
        string Name,
        string Description,
        string? ParentCode,
        bool IsActive = true);

    private sealed record ProductSeed(
        string Sku,
        string Name,
        string Unit,
        int ShelfLifeHours,
        int NearDateThresholdHours,
        string CategoryCode,
        string Status,
        string? ReplacesSku = null);

    private sealed record WarehouseSeed(
        string Code,
        string Name,
        string Type,
        string Address,
        decimal Latitude,
        decimal Longitude,
        int MinimumStockThresholdCartons);

    private sealed record RegionSeed(
        string Code,
        string Name,
        string Description,
        bool IsActive);

    private sealed record SalesGroupSeed(
        string Code,
        string Name,
        string RegionCode,
        bool IsActive);

    private sealed record RetailerSeed(
        string Code, string Name, string ContactName, string Phone, string Address,
        string RegionCode, string SalesGroupCode, string Status, string RetailerType,
        decimal ForecastRevenue, decimal StoreSize, decimal Capacity, int Frequency,
        int CreditTermDays, string PotentialClass, string Note);

    private sealed record InventorySeed(
        string ReceiptCode, string WarehouseCode, string Sku, DateOnly ExpirationDate,
        decimal OnHand, decimal Reserved, string SourceLotCode, string ExpiryStatus);

    private sealed record QrLabelSeed(
        string QrCode, string BatchCode, decimal Quantity, string? ParentQrCode,
        string Status, string Note);

    private sealed record ConfigurationSeed(
        string Key,
        string Value,
        string ValueType,
        string Description);
}

