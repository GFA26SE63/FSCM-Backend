using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Storage;
using FSCM.Application.Features.EntityQueries;
using FSCM.Persistence.EntityQueries;
using FSCM.Domain.Authorization;
using FSCM.IntegrationTests.Api;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Inventory;
using FSCM.Persistence.Refunds;
using FSCM.Persistence.Retailers;
using FSCM.Persistence.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Persistence;

public sealed class ReadModelAuditTests
{
    [Fact]
    public async Task Batched_scopes_and_redemption_totals_require_their_original_operation_grants()
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        var actor = factory.AddTestUser("read-audit@test.invalid", SystemRoles.Operator);
        factory.SetPermissions(SystemRoles.Operator, "api:Users.List");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(actor));
        foreach (var field in new[] { "userScopeBatchReads(ids: []) { userId }", "rewardRedemptionTotals(ids: []) { loyaltyRewardId quantity }" })
        {
            using var response = await client.PostAsJsonAsync("/graphql", new { query = "{ entities { " + field + " } }" });
            Assert.Contains("AUTH_NOT_AUTHORIZED", await response.Content.ReadAsStringAsync());
        }
    }

    [ConfiguredDatabaseFact]
    public async Task Batched_identity_scopes_match_authoritative_scope_for_every_existing_role()
    {
        var counter = new Counter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(AppDatabaseConfiguration.ReadConnectionString()).AddInterceptors(counter).Options);
        var users = (await db.UserAccountRecords.AsNoTracking().ToArrayAsync()).GroupBy(u => u.Role)
            .SelectMany(g => g.OrderBy(u => u.IsActive).Take(2)).ToArray();
        counter.Count = 0;
        var store = new IdentityStore(db);
        var scopes = await store.GetScopesAsync(users.Select(u => u.Id).ToArray(), default);
        Assert.Equal(8, counter.Count);
        foreach (var user in users)
        {
            var expected = await store.GetScopeAsync(user.Id, default);
            var actual = scopes[user.Id];
            Assert.Equal(expected.RegionIds.Order(), actual.RegionIds.Order());
            Assert.Equal(expected.SalesGroupIds.Order(), actual.SalesGroupIds.Order());
            Assert.Equal(expected.WarehouseIds.Order(), actual.WarehouseIds.Order());
            Assert.Equal(expected.RetailerIds.Order(), actual.RetailerIds.Order());
        }
    }

    [ConfiguredDatabaseFact]
    public async Task Receipt_and_refund_lists_match_detail_and_keep_a_constant_query_budget()
    {
        var counter = new Counter();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(AppDatabaseConfiguration.ReadConnectionString()).AddInterceptors(counter).Options);
        var inventory = new InventoryStore(db);
        var receipts = await inventory.ListReceiptsAsync(Guid.Empty, SystemRoles.Admin, null, default);
        Assert.Equal(2, counter.Count);
        foreach (var row in receipts.Take(2))
            Assert.Equal(JsonSerializer.Serialize(await inventory.FindReceiptAsync(row.Receipt.Id, default)), JsonSerializer.Serialize(row));
        Assert.Empty(await inventory.ListReceiptsAsync(Guid.NewGuid(), SystemRoles.WarehouseKeeper, null, default));
        counter.Count = 0;
        var refunds = new RefundStore(db);
        var rows = await refunds.ListAsync(null, null, Guid.Empty, SystemRoles.Admin, default);
        Assert.Equal(2, counter.Count);
        foreach (var row in rows.Take(2))
        {
            Assert.Equal(JsonSerializer.Serialize(await refunds.FindAsync(row.Refund.Id, false, default)), JsonSerializer.Serialize(row));
            var filtered = await refunds.ListAsync(row.Order.Id, row.Refund.Status, Guid.Empty, SystemRoles.Admin, default);
            var same = Assert.Single(filtered, r => r.Refund.Id == row.Refund.Id);
            Assert.Equal(row.PendingRefundAmount, same.PendingRefundAmount);
            Assert.Equal(row.RefundedAmount, same.RefundedAmount);
        }
        Assert.Empty(await refunds.ListAsync(null, null, Guid.NewGuid(), SystemRoles.WarehouseKeeper, default));
        var retailerStore = new RetailerStore(db);
        var retailers = await retailerStore.ListAsync(Guid.Empty, SystemRoles.Admin, null, default);
        var ids = retailers.Take(3).Select(r => r.Retailer.Id).ToArray();
        var selected = await retailerStore.ListSelectedAsync(Guid.Empty, SystemRoles.Admin, ids, default);
        Assert.Equal(ids.Order(), selected.Select(r => r.Retailer.Id).Order());
        Assert.Empty(await retailerStore.ListSelectedAsync(Guid.NewGuid(), SystemRoles.Sales, ids, default));
    }

    [ConfiguredDatabaseFact]
    public async Task Graphql_resolves_reward_image_without_per_item_detail_reads_and_keeps_retailer_id_filters()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var reward = await db.LoyaltyRewardRecords.FirstAsync();
        reward.ImageUrl = "reward/audit-image";
        await db.SaveChangesAsync();
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        using var factory = identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>(); services.AddSingleton(db);
            services.RemoveAll<IEntityQueryService>(); services.AddScoped<IEntityQueryService, EntityQueryService>();
            services.RemoveAll<IImageStorage>(); services.AddSingleton<IImageStorage>(new SigningImages());
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.Admin));
        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($id: UUID!) { entities { loyaltyRewards(first: 1, where: { id: { eq: $id } }) { nodes { id resolvedImageUrl } } } }",
            variables = new { id = reward.Id }
        });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        Assert.Equal("https://images.test/reward/audit-image", json.RootElement.GetProperty("data").GetProperty("entities")
            .GetProperty("loyaltyRewards").GetProperty("nodes")[0].GetProperty("resolvedImageUrl").GetString());
        var aggregateResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($ids: [UUID!]!) { entities { rewardRedemptionTotals(ids: $ids) { loyaltyRewardId quantity } } }",
            variables = new { ids = new[] { reward.Id } }
        });
        using var aggregateJson = JsonDocument.Parse(await aggregateResponse.Content.ReadAsStringAsync());
        Assert.False(aggregateJson.RootElement.TryGetProperty("errors", out _), aggregateJson.RootElement.ToString());
        var history = await db.RewardRedemptionRecords.AsNoTracking().Where(r => r.LoyaltyRewardId == reward.Id && r.Status == "Completed").ToArrayAsync();
        var totals = aggregateJson.RootElement.GetProperty("data").GetProperty("entities").GetProperty("rewardRedemptionTotals");
        if (history.Length == 0) Assert.Equal(0, totals.GetArrayLength());
        else
        {
            var quantities = history.Select(r => (decimal?)r.Quantity ?? (r.RequiredPointsSnapshot > 0 ? (decimal?)r.RedeemedPoints / r.RequiredPointsSnapshot : null)).ToArray();
            if (quantities.Any(q => q == null || q % 1 != 0)) Assert.Equal(JsonValueKind.Null, totals[0].GetProperty("quantity").ValueKind);
            else Assert.Equal(quantities.Sum(), totals[0].GetProperty("quantity").GetDecimal());
        }
        var retailerId = await db.RetailerRecords.Select(r => r.Id).FirstAsync();
        var lookup = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($ids: [UUID!]) { entities { retailerProfiles(ids: $ids, page: 1, pageSize: 100) { totalCount items { id contactName salesGroupCode } } } }",
            variables = new { ids = new[] { retailerId } }
        });
        using var lookupJson = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        Assert.False(lookupJson.RootElement.TryGetProperty("errors", out _), lookupJson.RootElement.ToString());
        Assert.All(lookupJson.RootElement.GetProperty("data").GetProperty("entities").GetProperty("retailerProfiles")
            .GetProperty("items").EnumerateArray(), r => Assert.Equal(retailerId, r.GetProperty("id").GetGuid()));
        await transaction.RollbackAsync();
    }

    private sealed class SigningImages : IImageStorage
    {
        public bool IsConfigured => true;
        public string GetReadUrl(ImageCategory category, string blobKey) => "https://images.test/" + blobKey;
        public Task<StoredImage> UploadAsync(ImageCategory category, ImageUpload upload, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(ImageCategory category, string blobKey, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Counter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; return ValueTask.FromResult(result); }
    }
}
