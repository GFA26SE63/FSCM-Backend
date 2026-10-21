using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.ManualPayments;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class PaymentApiTests
{
    [Fact]
    public async Task Manual_payment_rest_and_graphql_share_scope_and_canonical_ledger()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestFinanceStore();
        using var factory = CreateFactory(identityFactory, store);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Sales, identityFactory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "api-manual-001");
        var denied = await client.PostAsJsonAsync("/api/v1/manual-payments", new
        {
            salesOrderId = store.Order.Id,
            method = ManualPaymentMethods.PrepaidTransfer,
            amount = store.Ledger.Amount,
            markPaid = false,
            note = "Chờ đối soát chuyển khoản"
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);
        var createdResponse = await client.PostAsJsonAsync("/api/v1/manual-payments", new
        {
            salesOrderId = store.Order.Id,
            method = ManualPaymentMethods.PrepaidTransfer,
            amount = store.Ledger.Amount,
            markPaid = false,
            note = "Chờ đối soát chuyển khoản"
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync()).RootElement;
        var paymentId = created.GetProperty("id").GetGuid();

        var confirmResponse = await client.PostAsJsonAsync(
            $"/api/v1/manual-payments/{paymentId}/confirm",
            new { expectedVersion = 0, note = "Đã đối soát đủ tiền" });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Equal(store.Ledger.Amount, store.Ledger.ConfirmedAmount);

        var graphQlResponse = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query($id: UUID!, $orderId: UUID!) { manualPayment(id: $id) { id status outstandingAmount } manualPaymentsBySalesOrder(salesOrderId: $orderId, first: 20) { totalCount nodes { id method status totalConfirmedAmount } } }",
            variables = new { id = paymentId, orderId = store.Order.Id }
        });
        Assert.Equal(HttpStatusCode.OK, graphQlResponse.StatusCode);
        var graphQl = JsonDocument.Parse(await graphQlResponse.Content.ReadAsStringAsync());
        Assert.False(graphQl.RootElement.TryGetProperty("errors", out _), graphQl.RootElement.ToString());
        var data = graphQl.RootElement.GetProperty("data");
        Assert.Equal("Paid", data.GetProperty("manualPayment").GetProperty("status").GetString());
        Assert.Equal(1, data.GetProperty("manualPaymentsBySalesOrder").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Payos_checkout_requires_final_approval_and_webhook_is_idempotent()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new TestFinanceStore();
        store.Ledger.FinalApprovedAt = default;
        store.Ledger.FinalApprovalReference = string.Empty;
        using var factory = CreateFactory(identityFactory, store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.Admin, identityFactory);

        var blocked = await client.PostAsJsonAsync(
            $"/api/v1/payments/orders/{store.Order.Id}/payos-checkout", new { expectedVersion = 0 });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        store.Ledger.FinalApprovedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        store.Ledger.FinalApprovalReference = "RUN-20261021-01";
        var checkoutResponse = await client.PostAsJsonAsync(
            $"/api/v1/payments/orders/{store.Order.Id}/payos-checkout", new { expectedVersion = 0 });
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        var checkout = JsonDocument.Parse(await checkoutResponse.Content.ReadAsStringAsync()).RootElement;

        client.DefaultRequestHeaders.Authorization = null;
        var webhook = new
        {
            code = "00",
            desc = "success",
            success = true,
            data = new
            {
                orderCode = checkout.GetProperty("orderCode").GetInt64(),
                amount = decimal.ToInt64(store.Ledger.Amount),
                description = $"FSCM {store.Order.OrderCode}",
                accountNumber = "123456789",
                reference = "FT-PAYOS-API-001",
                transactionDateTime = "2026-10-21 16:00:00",
                currency = "VND",
                paymentLinkId = checkout.GetProperty("paymentLinkId").GetString(),
                code = "00",
                desc = "Thành công"
            },
            signature = "verified-by-test-gateway"
        };
        var first = await client.PostAsJsonAsync("/api/v1/payments/payos/webhook", webhook);
        var replay = await client.PostAsJsonAsync("/api/v1/payments/payos/webhook", webhook);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(store.Ledger.Amount, store.Ledger.ConfirmedAmount);
        Assert.Equal(PaymentStatuses.Paid, store.Ledger.Status);
        Assert.Equal(2, store.Ledger.Version);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        IdentityAuthorizationTests.IdentityApiFactory identityFactory,
        TestFinanceStore store) => identityFactory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentStore>();
            services.AddSingleton<IPaymentStore>(store);
            services.RemoveAll<IManualPaymentStore>();
            services.AddSingleton<IManualPaymentStore>(store);
            services.RemoveAll<IPayOsGateway>();
            services.AddSingleton<IPayOsGateway, TestPayOsGateway>();
        }));

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestFinanceStore : IPaymentStore, IManualPaymentStore
    {
        public TestFinanceStore()
        {
            Retailer = new Retailer
            {
                Id = Guid.NewGuid(),
                Code = "10004127",
                Name = "Cửa hàng An Phú"
            };
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = "DH-001063",
                RetailerId = Retailer.Id,
                RetailerCodeSnapshot = Retailer.Code,
                RetailerNameSnapshot = Retailer.Name,
                CreatedByUserId = Guid.NewGuid(),
                SalesGroupId = Guid.NewGuid(),
                PaymentMethod = OrderPaymentMethods.Prepaid,
                FinalAmount = 350_000m
            };
            Ledger = new Payment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order.Id,
                Method = Order.PaymentMethod,
                Status = PaymentStatuses.Unpaid,
                Amount = Order.FinalAmount,
                ConfirmedAmount = 0,
                FinalApprovedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                FinalApprovalReference = "RUN-20261021-01",
                Version = 0,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
        }

        public Retailer Retailer { get; }
        public SalesOrder Order { get; }
        public Payment Ledger { get; }
        public List<ManualPayment> ManualPayments { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public Task<PaymentView?> FindByOrderAsync(Guid orderId, bool forUpdate,
            CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(orderId == Order.Id ? new(Ledger, Order, Retailer) : null);

        Task<PaymentView?> IPaymentStore.FindByOperationKeyAsync(
            string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(Ledger.OperationKey == operationKey
                ? new(Ledger, Order, Retailer) : null);

        public Task<PaymentView?> FindByPayOsOrderCodeAsync(
            long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(Ledger.PayOsOrderCode == payOsOrderCode
                ? new(Ledger, Order, Retailer) : null);

        public Task<IReadOnlyCollection<PaymentView>> ListAsync(
            Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PaymentView>>([new(Ledger, Order, Retailer)]);

        public Task<bool> CanViewAsync(
            PaymentView view, Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult(actorRole is SystemRoles.Admin or SystemRoles.Operator ||
                actorRole == SystemRoles.Retailer && Retailer.UserAccountId == actorUserId);

        public void Add(Payment payment) => throw new NotSupportedException();
        void IPaymentStore.AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        Task IPaymentStore.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public void ClearTracking() { }

        public Task<SalesOrder?> FindOrderAsync(
            Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesOrder?>(salesOrderId == Order.Id ? Order : null);
        public Task<ManualPayment?> FindForUpdateAsync(
            Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(ManualPayments.SingleOrDefault(item => item.Id == paymentId));
        public Task<ManualPaymentView?> FindAsync(
            Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(ManualPayments.SingleOrDefault(item => item.Id == paymentId)));
        public Task<ManualPaymentView?> FindByOperationKeyAsync(
            string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(ManualPayments.SingleOrDefault(item => item.OperationKey == operationKey)));
        public Task<IReadOnlyCollection<ManualPaymentView>> ListByOrderAsync(
            Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ManualPaymentView>>(ManualPayments
                .Where(item => item.SalesOrderId == salesOrderId).Select(item => ToView(item)!).ToArray());
        public Task<bool> CanAccessOrderAsync(
            Guid userId, string role, SalesOrder order, CancellationToken cancellationToken) =>
            Task.FromResult(role is SystemRoles.Admin or SystemRoles.Operator ||
                role == SystemRoles.Sales && order.CreatedByUserId == userId ||
                role == SystemRoles.Retailer && Retailer.UserAccountId == userId);
        public void Add(ManualPayment payment) => ManualPayments.Add(payment);
        public void AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private ManualPaymentView? ToView(ManualPayment? payment) => payment is null
            ? null
            : new(payment, Order.OrderCode, Ledger.Amount, Ledger.ConfirmedAmount);
    }

    private sealed class TestPayOsGateway : IPayOsGateway
    {
        public Task<PayOsLinkData> CreatePaymentLinkAsync(
            PayOsCreateLinkData data, CancellationToken cancellationToken) =>
            Task.FromResult(new PayOsLinkData(data.OrderCode, data.Amount, "payos-api-link",
                "https://pay.payos.vn/web/api-test", "PAYOS-API-QR", "PENDING", 1_797_000_000));

        public Task<PayOsVerifiedWebhook> VerifyWebhookAsync(
            PayOsWebhookEnvelope webhook, CancellationToken cancellationToken) =>
            Task.FromResult(new PayOsVerifiedWebhook(webhook.Code, webhook.Success, webhook.Data));
    }
}
