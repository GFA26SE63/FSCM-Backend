namespace FSCM.API.Contracts.Quotes;

public sealed record AcceptQuoteRequest(int ExpectedVersion);
public sealed record RejectQuoteRequest(int ExpectedVersion, string Reason);
