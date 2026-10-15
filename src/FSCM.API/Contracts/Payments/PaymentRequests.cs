namespace FSCM.API.Contracts.Payments;

public sealed record CollectPaymentRequest(
    string CollectionMethod,
    int ExpectedVersion,
    string? Note);
