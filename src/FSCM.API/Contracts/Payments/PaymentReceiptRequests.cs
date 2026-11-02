namespace FSCM.API.Contracts.Payments;

public sealed record PaymentReceiptAllocationRequest(Guid PaymentId, decimal Amount);
public sealed record ConfirmPaymentReceiptRequest(Guid RetailerId, decimal ReceivedAmount, string? Reference, string? Note, IReadOnlyCollection<PaymentReceiptAllocationRequest> Allocations);
public sealed record ReversePaymentReceiptRequest(string Reason);
