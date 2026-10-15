using FSCM.Application.Features.Payments;
using Microsoft.Extensions.Configuration;
using PayOS;
using PayOS.Exceptions;
using PayOS.Models;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;

namespace FSCM.Infrastructure.Payments;

public sealed class PayOsGateway : IPayOsGateway
{
    private readonly string clientId;
    private readonly string apiKey;
    private readonly string checksumKey;
    private readonly string? partnerCode;
    private readonly string returnUrl;
    private readonly string cancelUrl;

    public PayOsGateway(IConfiguration configuration)
    {
        clientId = configuration["PayOS:ClientId"] ?? string.Empty;
        apiKey = configuration["PayOS:ApiKey"] ?? string.Empty;
        checksumKey = configuration["PayOS:ChecksumKey"] ?? string.Empty;
        partnerCode = configuration["PayOS:PartnerCode"];
        returnUrl = configuration["PayOS:ReturnUrl"] ?? string.Empty;
        cancelUrl = configuration["PayOS:CancelUrl"] ?? string.Empty;
    }

    public async Task<PayOsLinkData> CreatePaymentLinkAsync(
        PayOsCreateLinkData data,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration(requireRedirectUrls: true);
        try
        {
            using var client = CreateClient();
            var response = await client.PaymentRequests.CreateAsync(new CreatePaymentLinkRequest
            {
                OrderCode = data.OrderCode,
                Amount = data.Amount,
                Description = data.Description.Length <= 25 ? data.Description : data.Description[..25],
                BuyerName = data.BuyerName,
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl
            }, new RequestOptions<CreatePaymentLinkRequest> { CancellationToken = cancellationToken });
            return new(response.OrderCode, response.Amount, response.PaymentLinkId,
                response.CheckoutUrl, response.QrCode,
                PaymentLinkStatusConverter.ToSerializedString(response.Status), response.ExpiredAt);
        }
        catch (PayOSException exception)
        {
            throw PaymentOperationException.Gateway($"payOS could not create a checkout: {exception.Message}");
        }
    }

    public async Task<PayOsVerifiedWebhook> VerifyWebhookAsync(
        PayOsWebhookEnvelope webhook,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration(requireRedirectUrls: false);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var client = CreateClient();
            var verified = await client.Webhooks.VerifyAsync(new Webhook
            {
                Code = webhook.Code,
                Description = webhook.Description,
                Success = webhook.Success,
                Signature = webhook.Signature,
                Data = ToSdkWebhookData(webhook.Data)
            });
            return new(webhook.Code, webhook.Success, ToApplicationWebhookData(verified));
        }
        catch (PayOSException exception)
        {
            throw PaymentOperationException.Validation($"Invalid payOS webhook: {exception.Message}");
        }
    }

    private PayOSClient CreateClient() => new(new PayOSOptions
    {
        ClientId = clientId,
        ApiKey = apiKey,
        ChecksumKey = checksumKey,
        PartnerCode = string.IsNullOrWhiteSpace(partnerCode) ? null : partnerCode,
        TimeoutMs = 30_000,
        MaxRetries = 2
    });

    private void ValidateConfiguration(bool requireRedirectUrls)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(checksumKey))
            throw PaymentOperationException.Gateway(
                "payOS credentials are not configured. Set PayOS:ClientId, ApiKey, and ChecksumKey.");
        if (requireRedirectUrls && (!Uri.TryCreate(returnUrl, UriKind.Absolute, out _) ||
                                    !Uri.TryCreate(cancelUrl, UriKind.Absolute, out _)))
            throw PaymentOperationException.Gateway(
                "payOS ReturnUrl and CancelUrl must be configured as absolute URLs.");
    }

    private static WebhookData ToSdkWebhookData(PayOsWebhookPayload data) => new()
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

    private static PayOsWebhookPayload ToApplicationWebhookData(WebhookData data) => new(
        data.OrderCode, data.Amount, data.Description, data.AccountNumber, data.Reference,
        data.TransactionDateTime, data.Currency, data.PaymentLinkId, data.Code,
        data.Description2, data.CounterAccountBankId, data.CounterAccountBankName,
        data.CounterAccountName, data.CounterAccountNumber, data.VirtualAccountName,
        data.VirtualAccountNumber);
}
