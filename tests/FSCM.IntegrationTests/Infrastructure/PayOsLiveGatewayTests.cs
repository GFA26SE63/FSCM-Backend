using FSCM.Application.Features.Payments;
using FSCM.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;

namespace FSCM.IntegrationTests.Infrastructure;

public sealed class PayOsLiveGatewayTests
{
    [Fact]
    public async Task CreatePaymentLinkAsync_returns_consistent_live_payos_link()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_PAYOS_LIVE_TESTS"), "1",
                StringComparison.Ordinal))
            return;

        var settingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "FSCM.API", "appsettings.json"));
        var configuration = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();
        var gateway = new PayOsGateway(configuration);
        var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await gateway.CreatePaymentLinkAsync(new(
            orderCode, 2_000, "FSCM TEST", "FSCM Integration Test"), CancellationToken.None);

        Assert.Equal(orderCode, result.OrderCode);
        Assert.Equal(2_000, result.Amount);
        Assert.False(string.IsNullOrWhiteSpace(result.PaymentLinkId));
        Assert.True(Uri.TryCreate(result.CheckoutUrl, UriKind.Absolute, out var checkoutUri));
        Assert.Equal(Uri.UriSchemeHttps, checkoutUri!.Scheme);
    }
}
