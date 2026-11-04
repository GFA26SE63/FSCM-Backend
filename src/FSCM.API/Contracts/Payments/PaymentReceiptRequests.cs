namespace FSCM.API.Contracts.Payments;

public sealed record PaymentReceiptAllocationRequest(Guid PaymentId, decimal Amount);
public sealed record ConfirmPaymentReceiptRequest(Guid RetailerId, decimal ReceivedAmount, string? Reference, string? Note, IReadOnlyCollection<PaymentReceiptAllocationRequest> Allocations);
public sealed record ReversePaymentReceiptRequest(string Reason);

/// <summary>F5 step 4: ask the server to propose how to split a received amount across invoices.</summary>
public sealed record ProposeReceiptAllocationRequest(Guid RetailerId, decimal ReceivedAmount);
