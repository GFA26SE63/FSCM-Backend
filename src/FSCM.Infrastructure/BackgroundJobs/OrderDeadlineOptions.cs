namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class OrderDeadlineOptions
{
    public const string SectionName = "BackgroundJobs:OrderDeadline";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 5;
    public int PrepaidPaymentWindowDays { get; set; } = 3;
}
