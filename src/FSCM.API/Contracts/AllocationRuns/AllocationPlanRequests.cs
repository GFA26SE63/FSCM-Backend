namespace FSCM.API.Contracts.AllocationRuns;

public sealed record ApprovePlanVersionRequest(int ExpectedVersion);

public sealed record RequestRecalculationRequest(int ExpectedVersion, string Reason);
