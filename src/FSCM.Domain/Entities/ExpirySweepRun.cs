namespace FSCM.Domain.Entities;

public sealed class ExpirySweepRun
{
    public Guid Id { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int BatchesScanned { get; set; }
    public int StatusesUpdated { get; set; }
    public int BatchesExpired { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
