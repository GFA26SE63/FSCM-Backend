namespace FSCM.API.Contracts.Quotes;

public sealed record AcceptQuoteRequest(int ExpectedVersion, string PaymentMethod);
public sealed record RejectQuoteRequest(int ExpectedVersion, string Reason);
