using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class RetailerReceivableApiTests
{
    [Fact]
    public async Task Rest_and_graphql_share_retailer_scoped_canonical_receivable()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var retailer = identityFactory.AddTestUser("retailer.receivable@test.local", SystemRoles.Retailer);
        var otherRetailer = identityFactory.AddTestUser("retailer.other@test.local", SystemRoles.Retailer);
        var store = new TestStore(retailer.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRetailerReceivableStore>();
            services.AddSingleton<IRetailerReceivableStore>(store);
        }));
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(retailer, identityFactory);
        var rest = await client.GetAsync("/api/v1/retailer/receivables?page=1&pageSize=10&overdue=true");
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;
        var restItem = restBody.GetProperty("items")[0];
        Assert.Equal(store.Order.Id, restItem.GetProperty("orderId").GetGuid());
        Assert.Equal(150_000, restItem.GetProperty("collectibleAmount").GetDecimal());
        Assert.Equal(50_000, restItem.GetProperty("pendingDisputedAmount").GetDecimal());
        Assert.Single(restItem.GetProperty("refunds").EnumerateArray());

        var graphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ retailerReceivables(filter: { page: 1, pageSize: 10, overdue: true }) { totalCount items { orderId collectibleAmount pendingDisputedAmount refunds { status } } } }"
        });
        Assert.Equal(HttpStatusCode.OK, graphQl.StatusCode);
        var graphBody = JsonDocument.Parse(await graphQl.Content.ReadAsStringAsync()).RootElement;
        Assert.False(graphBody.TryGetProperty("errors", out _), graphBody.ToString());
        var graphItem = graphBody.GetProperty("data").GetProperty("retailerReceivables")
            .GetProperty("items")[0];
        Assert.Equal(restItem.GetProperty("orderId").GetGuid(), graphItem.GetProperty("orderId").GetGuid());
        Assert.Equal(restItem.GetProperty("collectibleAmount").GetDecimal(),
            graphItem.GetProperty("collectibleAmount").GetDecimal());

        client.DefaultRequestHeaders.Authorization = Bearer(otherRetailer, identityFactory);
        var hidden = await client.GetAsync($"/api/v1/retailer/receivables/orders/{store.Order.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        var denied = await client.GetAsync("/api/v1/retailer/receivables");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestStore
        : IRetailerReceivableStore
    {
        private readonly Guid retailerUserId;
        private readonly RetailerReceivableView view;

        public TestStore(Guid retailerUserId)
        {
            this.retailerUserId = retailerUserId;
            Order = new SalesOrder { Id = Guid.NewGuid(), OrderCode = "DH-001068" };
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order.Id,
                Method = "Credit",
                Status = PaymentStatuses.Unpaid,
                Amount = 1_000_000,
                AdjustedAmount = 900_000,
                ConfirmedAmount = 700_000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            view = new(new(payment, Order, new Retailer { UserAccountId = retailerUserId }, 40_000, 0, true),
                [new(Guid.NewGuid(), "BankTransfer", 700_000, "Paid", DateTimeOffset.UtcNow, "FT-001", null)],
                [new(Guid.NewGuid(), "Open", 50_000, DateTimeOffset.UtcNow, null, "Đang xem xét")],
                [new(Guid.NewGuid(), 40_000, "AwaitingVerification", null, null, null, "Chờ hoàn")]);
        }

        public SalesOrder Order { get; }

        public Task<IReadOnlyCollection<RetailerReceivableView>> ListAsync(
            Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RetailerReceivableView>>(
                userId == retailerUserId ? [view] : []);

        public Task<RetailerReceivableView?> FindByOrderAsync(
            Guid userId, Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<RetailerReceivableView?>(
                userId == retailerUserId && orderId == Order.Id ? view : null);
    }
}
