namespace FSCM.API.Contracts.AllocationRuns;

/// <summary>
/// F85 recovery: run today's allocation by hand. Leave the cutoff empty to use the published one;
/// pass a later moment on the same business date only when the run was down and orders kept coming.
/// </summary>
public sealed record StartAllocationRunRequest(DateTimeOffset? CutoffAt);
