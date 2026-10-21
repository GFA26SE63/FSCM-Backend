namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class InventoryOutboxOptions
{
    public const string SectionName = "BackgroundJobs:InventoryOutbox";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
}
