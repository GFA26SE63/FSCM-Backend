namespace FSCM.API.Contracts.Orders;

public sealed record CreateAssistedOrderRequest(
    Guid ClientOrderKey,
    Guid RetailerId,
    string PaymentMethod,
    DateOnly? RequestedDeliveryDate,
    string? DeliveryAddress,
    string? Note,
    IReadOnlyCollection<CreateAssistedOrderLineRequest> Lines);

public sealed record CreateAssistedOrderLineRequest(Guid ProductId, int Quantity);

public sealed record OrderDecisionRequest(int ExpectedVersion);

public sealed record RejectOrderRequest(int ExpectedVersion, string Reason);

public sealed record CancelOrderRequest(int ExpectedVersion, string? Reason);
