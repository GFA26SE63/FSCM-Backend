using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Payments;

public static class RetailerDebtCheckoutModes
{
    public const string Selected = "Selected";
    public const string All = "All";
}

public sealed record CreateRetailerDebtPayOsCheckoutData(
    string Mode,
    IReadOnlyCollection<Guid> PaymentIds);

public sealed record RetailerDebtCheckoutAllocationResult(
    Guid PaymentId,
    Guid OrderId,
    string OrderCode,
    decimal Amount);

public sealed record RetailerDebtPayOsCheckoutResult(
    Guid Id,
    string Status,
    decimal Amount,
    long PayOsOrderCode,
    string PaymentLinkId,
    string CheckoutUrl,
    string QrCode,
    long? ExpiredAt,
    string? PayOsReference,
    Guid? PaymentReceiptId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<RetailerDebtCheckoutAllocationResult> Allocations);

public sealed record RetailerDebtCheckoutView(
    RetailerDebtPayOsCheckout Checkout,
    Retailer Retailer,
    IReadOnlyCollection<RetailerDebtPayOsAllocation> Allocations,
    IReadOnlyCollection<ReceiptPaymentView> Payments);

public interface IRetailerDebtPayOsStore
{
    Task<Retailer?> FindRetailerByUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ReceiptPaymentView>> ListCollectibleForUpdateAsync(
        Guid retailerId, IReadOnlyCollection<Guid>? paymentIds, CancellationToken cancellationToken);
    Task<RetailerDebtCheckoutView?> FindByOperationKeyAsync(
        string operationKey, bool forUpdate, CancellationToken cancellationToken);
    Task<RetailerDebtCheckoutView?> FindByPayOsOrderCodeAsync(
        long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken);
    Task<RetailerDebtCheckoutView?> FindByIdForRetailerAsync(
        Guid checkoutId, Guid retailerId, CancellationToken cancellationToken);
    void AddCheckout(RetailerDebtPayOsCheckout checkout);
    void AddAllocations(IEnumerable<RetailerDebtPayOsAllocation> allocations);
    void AddReceipt(PaymentReceipt receipt, IEnumerable<PaymentReceiptAllocation> allocations);
    void AddAuditLog(AuditLog auditLog);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    void ClearTracking();
}
