using System.Data;
using System.Globalization;
using System.Text.Json;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Reports;
using Microsoft.EntityFrameworkCore;

// Reporting fixtures only. Existing accounts, balances, inventory and orders are never edited.
try
{
    await RunAsync(args);
}
catch (Exception error)
{
    Console.Error.WriteLine($"Seed failed ({error.GetType().Name}). No partial demo transaction was committed.");
    if (error is InvalidOperationException or ArgumentException or FormatException)
        Console.Error.WriteLine(error.Message);
    else if (error is Npgsql.PostgresException postgres)
        Console.Error.WriteLine($"PostgreSQL {postgres.SqlState}; table: {postgres.TableName}; constraint: {postgres.ConstraintName}. See the schema and migrations.");
    else
        Console.Error.WriteLine("Check database connectivity, schema migrations and required master data. Connection credentials are not logged.");
    Environment.ExitCode = 1;
}

static async Task RunAsync(string[] args)
{
    string? Option(string name) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    if (!args.Contains("--apply") && !args.Contains("--dry-run"))
        throw new ArgumentException("Use --dry-run to verify and roll back, or --apply to insert DEMO reporting data.");
    if (args.Contains("--apply") && args.Contains("--dry-run"))
        throw new ArgumentException("Choose either --apply or --dry-run.");
    var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
    var month = Option("--month") ?? today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    var from = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    if (from > today) throw new ArgumentException("Do not seed a future month.");
    var to = from.AddMonths(1).AddDays(-1);
    if (to > today) to = today;
    var code = "DEMO-RPT-" + from.ToString("yyyyMM");
    var note = code + " · DEMO reporting fixture; not a physical stock movement or real sale.";
    var configPath = Path.GetFullPath(Option("--config") ?? "src/FSCM.API/appsettings.json");
    var connection = Environment.GetEnvironmentVariable("FSCM_REPORT_SEED_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection))
    {
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        connection = config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
    }
    if (string.IsNullOrWhiteSpace(connection)) throw new ArgumentException("Missing database connection configuration.");
    var target = new Npgsql.NpgsqlConnectionStringBuilder(connection) { Timeout = 8 };
    Console.WriteLine($"Target: {target.Host} / {target.Database}; period: {from:yyyy-MM-dd}..{to:yyyy-MM-dd}; marker: {code}");
    Console.WriteLine("DEMO data will be visible in operational lists and report totals, labelled DEMO.");
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target.ConnectionString).Options);
    await db.Database.OpenConnectionAsync();
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(709131212)");
    if ((await db.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("Apply pending backend migrations before seeding reports.");
    if (!await db.LoyaltyProgramRecords.AnyAsync(p => p.Code == code))
    {
        var actor = await db.UserAccountRecords.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "Operator")).OrderBy(u => u.Role).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("An active Admin/Operator is required.");
        var group = await db.SalesGroupRecords.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.Code).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("An active Sales group and region are required.");
        var stock = await (from b in db.BatchRecords.AsNoTracking()
                           join p in db.ProductRecords.AsNoTracking() on b.ProductId equals p.Id
                           join price in db.ProductPriceRecords.AsNoTracking() on p.Id equals price.ProductId
                           where price.IsActive && price.UnitPrice > 0
                           orderby price.EffectiveFrom descending
                           select new { Batch = b, Product = p, Price = price }).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("An existing batch, product and active product price are required. No inventory is created or altered.");
        DateTimeOffset At(int day, int hour = 8) => new(from.Year, from.Month, Math.Min(day, to.Day), hour, 0, 0, TimeSpan.FromHours(7));
        // PostgreSQL timestamptz is always written in UTC.
        DateTimeOffset Utc(int day, int hour = 8) => At(day, hour).ToUniversalTime();
        var created = Utc(1);
        var program = new LoyaltyProgram { Id = Guid.NewGuid(), Code = code, Name = "[DEMO] Loyalty báo cáo " + month,
            Description = note, SpendAmountPerPoint = 1000, MinimumRedeemPoints = 100,
            StartAt = created.AddDays(-1), EndAt = created.AddMonths(1), PointsExpireAt = created.AddMonths(2),
            IsActive = false, CreatedByUserId = actor.Id, CreatedAt = created, UpdatedAt = created };
        var gift = new LoyaltyReward { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, Code = code + "-GIFT",
            Name = "[DEMO] Bình giữ nhiệt", Description = note, RewardType = "Gift", RequiredPoints = 100,
            AvailableQuantity = 40, IsActive = false, CreatedAt = created, UpdatedAt = created };
        var promotion = new Promotion { Id = Guid.NewGuid(), Code = code + "-PROMO", Name = "[DEMO] Khuyến mãi báo cáo " + month,
            PromotionType = "Percentage", MinimumQuantity = 1, DiscountPercent = 10, StartAt = created,
            EndAt = created.AddMonths(1), IsActive = false, CreatedByUserId = actor.Id, CreatedAt = created, UpdatedAt = created };
        db.AddRange(program, gift, promotion);
        var statuses = new[] { "Pending", "Approved", "Delivered", "Completed", "Rejected", "Cancelled" };
        for (var i = 0; i < 3; i++)
        {
            var retailer = new Retailer { Id = Guid.NewGuid(), RegionId = group.RegionId, Code = code + "-R" + (i + 1),
                Name = new[] { "[DEMO] Tạp hóa An Phú", "[DEMO] Cửa hàng Bình Minh", "[DEMO] Siêu thị mini Thành Công" }[i],
                Address = "Địa chỉ minh họa báo cáo", Status = "Active", RetailerType = "Grocery", StoreSizeCategory = "S",
                PurchaseFrequencyPerMonth = 4, CreditTermDays = 0, DeclaredByUserId = actor.Id,
                AssessmentNote = note, CreatedAt = created, UpdatedAt = created };
            var card = new RetailerLoyalty { Id = Guid.NewGuid(), RetailerId = retailer.Id, CreatedAt = created.AddDays(-1), UpdatedAt = Utc(10) };
            db.AddRange(retailer, card,
                new RetailerSalesAssignment { Id = Guid.NewGuid(), RetailerId = retailer.Id, SalesGroupId = group.Id, IsActive = true, AssignedAt = created },
                new LoyaltyProgramRetailer { Id = Guid.NewGuid(), RetailerId = retailer.Id, LoyaltyProgramId = program.Id });
            LoyaltyTransaction Ledger(string kind, int points, DateTimeOffset at, bool moves = true)
            {
                if (moves) card.CurrentPoints += points;
                var row = new LoyaltyTransaction { Id = Guid.NewGuid(), LoyaltyProgramId = program.Id, RetailerLoyaltyId = card.Id,
                    CreatedByUserId = actor.Id, TransactionType = kind, Points = points, BalanceAfter = card.CurrentPoints,
                    SpendAmountPerPointSnapshot = 1000, Note = note, CreatedAt = at };
                db.Add(row);
                return row;
            }
            Ledger("Earned", 2000, created.AddDays(-1));
            Ledger("Earned", 500, Utc(2));
            Ledger("PendingEarn", 250, Utc(3), false);
            Ledger("PendingEarnCancelled", -250, Utc(4), false);
            Ledger("EarnedAdjusted", -50, Utc(5));
            Ledger("Expired", -25, Utc(6));
            card.LifetimeEarnedPoints = 2500;
            for (var j = 0; j < 2; j++)
            {
                var status = statuses[i * 2 + j];
                var quantity = j + 1;
                var points = quantity * gift.RequiredPoints;
                var held = Ledger("RedemptionHeld", -points, Utc(7, 8 + j));
                card.HeldPoints += points;
                if (status is "Rejected" or "Cancelled")
                {
                    Ledger("RedemptionRejected", points, Utc(8, 8 + j));
                    card.HeldPoints -= points;
                }
                else
                {
                    gift.AvailableQuantity -= quantity;
                    if (status == "Completed")
                    {
                        Ledger("RedemptionCompleted", 0, Utc(10));
                        card.HeldPoints -= points;
                        card.LifetimeRedeemedPoints += points;
                    }
                }
                var approved = status is "Approved" or "Delivered" or "Completed";
                var delivered = status is "Delivered" or "Completed";
                db.Add(new RewardRedemption { Id = Guid.NewGuid(), RetailerId = retailer.Id, LoyaltyRewardId = gift.Id,
                    LoyaltyTransactionId = held.Id, ClientRequestId = code + "-" + i + "-" + j,
                    Quantity = quantity, RequiredPointsSnapshot = 100, RedeemedPoints = points, Status = status,
                    RedeemedByUserId = actor.Id, RedeemedAt = held.CreatedAt, CreatedAt = held.CreatedAt, UpdatedAt = Utc(10),
                    ApprovedByUserId = approved ? actor.Id : null, ApprovedAt = approved ? Utc(8) : null,
                    DeliveredByUserId = delivered ? actor.Id : null, DeliveredAt = delivered ? Utc(9) : null,
                    FulfilledAt = status == "Completed" ? Utc(10) : null,
                    CancelledAt = status is "Rejected" or "Cancelled" ? Utc(8, 8 + j) : null, Note = note });
            }
            var qty = 10m + i * 5;
            var list = stock.Price.UnitPrice;
            var campaign = i == 1 ? 0 : Math.Round(list * 0.10m, 2);
            var shelf = i == 0 ? 0 : Math.Round((list - campaign) * 0.05m, 2);
            var net = list - campaign - shelf;
            var orderStatus = new[] { "ConvertedToPo", "Delivered", "Completed" }[i];
            var at = Utc(8, 14 + i);
            var order = new SalesOrder { Id = Guid.NewGuid(), OrderCode = code + "-SO" + (i + 1), RetailerId = retailer.Id,
                RetailerCodeSnapshot = retailer.Code, RetailerNameSnapshot = retailer.Name, SalesGroupId = group.Id,
                CreatedByUserId = actor.Id, ClientOrderKey = Guid.NewGuid(), RequestHash = new string('d', 64),
                Source = "Assisted", Status = orderStatus, PaymentMethod = "Prepaid", DeliveryAddress = retailer.Address!, Note = note,
                SubtotalAmount = list * qty, DiscountAmount = (list - net) * qty, FinalAmount = net * qty,
                ServerReceivedAt = at, AcceptedAt = at, CreatedAt = at, UpdatedAt = at, Version = 1 };
            var line = new SalesOrderLine { Id = Guid.NewGuid(), SalesOrderId = order.Id, ProductId = stock.Product.Id,
                LineNumber = 1, SkuSnapshot = stock.Product.Sku, ProductNameSnapshot = stock.Product.Name,
                OrderedQuantity = qty, UnitPrice = list, ProductPriceId = stock.Price.Id,
                ProductPriceEffectiveFrom = stock.Price.EffectiveFrom, ProductPriceEffectiveTo = stock.Price.EffectiveTo,
                PricedAt = at, GrossAmount = list * qty, DiscountAmount = (list - net) * qty,
                NetAmount = net * qty, MlorSourceScope = "System", MlorMinimumLifeRatio = 0.5m, MlorResolvedAt = at, CreatedAt = at };
            var run = new AllocationRun { Id = Guid.NewGuid(), RunCode = code + "-RUN" + i, SalesOrderId = order.Id,
                AttemptNumber = 1, Status = "Succeeded", StartedAt = at, CompletedAt = at, SnapshotCompletedAt = at,
                CreatedAt = at, UpdatedAt = at, Version = 1 };
            var runOrder = new AllocationRunOrder { Id = Guid.NewGuid(), AllocationRunId = run.Id, SalesOrderId = order.Id,
                OrderCodeSnapshot = order.OrderCode, RetailerId = retailer.Id, AcceptedAt = at, PriorityRank = 1, Status = "Fulfilled", CreatedAt = at };
            var frozen = new AllocationRunLine { Id = Guid.NewGuid(), AllocationRunOrderId = runOrder.Id,
                SalesOrderLineId = line.Id, ProductId = stock.Product.Id, SkuSnapshot = stock.Product.Sku, LineNumber = 1,
                OrderedQuantity = qty, AllocatedQuantity = qty, TotalShelfLifeHours = stock.Product.ShelfLifeHours,
                EffectiveMlorRatio = 0.5m, MlorSource = "System", ListUnitPrice = list, BaseNetUnitPrice = list - campaign, CreatedAt = at };
            var price = new AllocationPlanPrice { Id = Guid.NewGuid(), AllocationRunId = run.Id, AllocationRunLineId = frozen.Id,
                BatchId = stock.Batch.Id, WarehouseId = stock.Batch.WarehouseId, Quantity = qty, ListUnitPrice = list,
                BaseNetUnitPrice = list - campaign, ShelfLifeDiscountPerUnit = shelf, FinalNetUnitPrice = net, Amount = net * qty,
                PricedAt = at, BatchExpiresAt = new DateTimeOffset(stock.Batch.ExpirationDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                TotalShelfLifeHours = stock.Product.ShelfLifeHours, RemainingShelfLifePercent = 50,
                DiscountTableVersion = 1, Explanation = note, CreatedAt = at };
            db.AddRange(order, line, run, runOrder, frozen, price,
                new SalesOrderQuote { Id = Guid.NewGuid(), SalesOrderId = order.Id, AllocationRunId = run.Id,
                    RevisionNumber = 1, Status = "Accepted", TotalAmount = net * qty, IssuedAt = at, ExpiresAt = at.AddDays(1),
                    AcceptedAt = at.AddMinutes(1), AcceptedByUserId = actor.Id, CreatedAt = at, UpdatedAt = at, Version = 1 },
                new OrderStatusHistory { Id = Guid.NewGuid(), SalesOrderId = order.Id, ChangedByUserId = actor.Id,
                    ToStatus = orderStatus, ChangedAt = at.AddMinutes(2), Reason = note });
            if (campaign > 0) db.Add(new OrderPromotion { Id = Guid.NewGuid(), SalesOrderLineId = line.Id,
                PromotionId = promotion.Id, PromotionCodeSnapshot = promotion.Code, PromotionNameSnapshot = promotion.Name,
                PromotionTypeSnapshot = promotion.PromotionType, MinimumQuantitySnapshot = 1,
                EffectiveFromSnapshot = promotion.StartAt, EffectiveToSnapshot = promotion.EndAt,
                DiscountPercent = 10, DiscountAmount = campaign * qty, IsServerFinal = true, AppliedAt = at });
        }
        await db.SaveChangesAsync();
    }
    else Console.WriteLine("This month's DEMO fixture already exists; no duplicate rows inserted.");
    var reports = new OperationsReportStore(db);
    foreach (var kind in new[] { "redemptions", "loyalty", "promotions" })
    {
        var report = await reports.ReadAsync(kind, new OperationsReportQuery(from, to, Retailer: code), default);
        if (report.TotalCount == 0) throw new InvalidOperationException($"Verification failed: {kind} is empty; rolling back.");
        Console.WriteLine($"Verified {kind}: {report.TotalCount} rows in {month}.");
    }
    if (args.Contains("--apply")) { await tx.CommitAsync(); Console.WriteLine("COMMITTED. Refresh deployed portal reports for this month; filter Retailer = " + code); }
    else { await tx.RollbackAsync(); Console.WriteLine("DRY RUN passed; rolled back all demo inserts."); }
}
