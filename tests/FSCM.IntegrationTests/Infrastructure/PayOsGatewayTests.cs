using FSCM.Application.Features.Payments;
using FSCM.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using PayOS;
using PayOS.Models.Webhooks;

namespace FSCM.IntegrationTests.Infrastructure;

public sealed class PayOsGatewayTests
{
    private const string ChecksumKey = "payos-integration-test-checksum-key";

    [Fact]
    public async Task VerifyWebhookAsync_accepts_valid_payos_signature()
    {
        var payload = CreatePayload();
        using var client = new PayOSClient("client-id", "api-key", ChecksumKey);
        var signature = client.Crypto.CreateSignatureFromObject(ToSdkData(payload), ChecksumKey)
            ?? throw new InvalidOperationException("Test signature could not be created.");
        var gateway = CreateGateway();

        var verified = await gateway.VerifyWebhookAsync(
            new("00", "success", true, payload, signature), default);

        Assert.True(verified.Success);
        Assert.Equal(payload.OrderCode, verified.Data.OrderCode);
        Assert.Equal(payload.Reference, verified.Data.Reference);
    }

    [Fact]
    public async Task VerifyWebhookAsync_rejects_invalid_signature()
    {
        var gateway = CreateGateway();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            gateway.VerifyWebhookAsync(
                new("00", "success", true, CreatePayload(), "invalid-signature"), default));

        Assert.Equal("validation_error", exception.Code);
    }

    private static PayOsGateway CreateGateway()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PayOS:ClientId"] = "client-id",
                ["PayOS:ApiKey"] = "api-key",
                ["PayOS:ChecksumKey"] = ChecksumKey,
                ["PayOS:ReturnUrl"] = "https://fscm.test/payos/success",
                ["PayOS:CancelUrl"] = "https://fscm.test/payos/cancel"
            }).Build();
        return new PayOsGateway(configuration);
    }

    private static PayOsWebhookPayload CreatePayload() => new(
        1_234_567, 2_318_400, "FSCM DH-001041", "123456789", "FT-PAYOS-001",
        "2026-10-15 10:05:00", "VND", "test-payment-link", "00", "Thành công",
        null, null, null, null, null, null);

    private static WebhookData ToSdkData(PayOsWebhookPayload data) => new()
    {
        OrderCode = data.OrderCode,
        Amount = data.Amount,
        Description = data.Description,
        AccountNumber = data.AccountNumber,
        Reference = data.Reference,
        TransactionDateTime = data.TransactionDateTime,
        Currency = data.Currency,
        PaymentLinkId = data.PaymentLinkId,
        Code = data.Code,
        Description2 = data.Description2,
        CounterAccountBankId = data.CounterAccountBankId,
        CounterAccountBankName = data.CounterAccountBankName,
        CounterAccountName = data.CounterAccountName,
        CounterAccountNumber = data.CounterAccountNumber,
        VirtualAccountName = data.VirtualAccountName,
        VirtualAccountNumber = data.VirtualAccountNumber
    };
}
