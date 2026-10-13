using FSCM.Application.Features.Identity;
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
    ILogger<DevelopmentDataSeeder> logger)
{
    private const string DemoWarehouseCode = "WH-HCM-01";
    private const string DemoRegionCode = "HCM";
    private const string DemoSalesGroupCode = "HCM-A";
    private const string DemoRetailerCode = "RTL-HCM-001";

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
        var retailer = await SeedRetailerAsync(regions[DemoRegionCode].Id, users[SystemRoles.Sales].Id, now, cancellationToken);
        var categories = await SeedCategoriesAsync(now, cancellationToken);
        var products = await SeedProductsAsync(categories, now, cancellationToken);
        var configuredWarehouses = await SeedWarehouseConfigurationDataAsync(
            warehouse, retailer, products, users[SystemRoles.Admin].Id, now, cancellationToken);

        await SeedWarehouseAssignmentsAsync(users, warehouse.Id, now, cancellationToken);
        await SeedSalesGroupMembershipsAsync(users, salesGroups, now, cancellationToken);
        await SeedRetailerAssignmentsAsync(retailer.Id, salesGroups, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Development data seeded: {UserCount} users, {CategoryCount} categories, {ProductCount} products, {WarehouseCount} warehouses, {RegionCount} regions, {SalesGroupCount} sales groups, retailer {RetailerCode}.",
            users.Count,
            categories.Count,
            products.Count,
            configuredWarehouses.Count,
            regions.Count,
            salesGroups.Count,
            DemoRetailerCode);
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
                    EntraObjectId = $"local:{userId:N}",
                    Email = definition.Email,
                    CreatedAt = now
                };
                dbContext.UserAccountRecords.Add(user);
            }

            user.FullName = definition.FullName;
            user.Role = definition.Role;
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
                "Đường Nguyễn Thị Rành, huyện Củ Chi, Thành phố Hồ Chí Minh", 10.9747000m, 106.4931000m),
            new WarehouseSeed("WH-TD", "Kho vùng Thủ Đức", WarehouseTypes.Regional,
                "Đường Võ Chí Công, thành phố Thủ Đức, Thành phố Hồ Chí Minh", 10.8480000m, 106.7720000m),
            new WarehouseSeed("WH-Q7", "Kho vùng Quận 7", WarehouseTypes.Regional,
                "Đường Nguyễn Văn Linh, Quận 7, Thành phố Hồ Chí Minh", 10.7298000m, 106.7219000m)
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
            location.UpdatedAt = now;
            warehouses.Add(warehouse);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var warehouse in warehouses)
        {
            if (!await dbContext.TransitTimeRuleRecords.AnyAsync(
                    item => item.WarehouseId == warehouse.Id && item.IsActive, cancellationToken))
            {
                dbContext.TransitTimeRuleRecords.AddRange(
                    CreateTransitRule(warehouse.Id, 0, 10_000, 60, 1_440, now),
                    CreateTransitRule(warehouse.Id, 10_001, 20_000, 60, 2_880, now));
            }
        }

        foreach (var definition in new[] { (Product: products["SKU001"], Ratio: 0.25m), (Product: products["SKU002"], Ratio: 0.30m) })
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
            new ConfigurationSeed(BusinessConfigurationKeys.RegionalMinimumStockCartons, "35", "integer",
                "Ngưỡng tồn kho tối thiểu tại kho vùng là 35 thùng"),
            new ConfigurationSeed(BusinessConfigurationKeys.DefaultMlorRatio, "0.25", "decimal",
                "Tỷ lệ hạn sử dụng còn lại tối thiểu mặc định là 25 phần trăm")
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
        int handlingMinutes, int transitMinutes, DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            MinDistanceMeters = minimumDistance,
            MaxDistanceMeters = maximumDistance,
            HandlingMinutes = handlingMinutes,
            TransitMinutes = transitMinutes,
            MaxServingDistanceMeters = 20_000,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
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
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var retailer = await dbContext.RetailerRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoRetailerCode,
            cancellationToken);
        if (retailer is null)
        {
            retailer = new Retailer { Id = Guid.NewGuid(), Code = DemoRetailerCode, CreatedAt = now };
            dbContext.RetailerRecords.Add(retailer);
        }

        retailer.RegionId = regionId;
        retailer.Name = "Cửa hàng An Phú";
        retailer.ContactName = "Nguyễn Văn Minh";
        retailer.Phone = "+84 28 0000 0001";
        retailer.Email = "cuahang.anphu@fscm.local";
        retailer.Address = "Phường An Phú, thành phố Thủ Đức, Thành phố Hồ Chí Minh";
        retailer.Status = "Active";
        retailer.DeclaredByUserId = declaredByUserId;
        retailer.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return retailer;
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
        decimal Longitude);

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

    private sealed record ConfigurationSeed(
        string Key,
        string Value,
        string ValueType,
        string Description);
}

