namespace FSCM.Infrastructure.BackgroundJobs;

/// <summary>
/// Configuration for the daily allocation run. Bound from the <c>BackgroundJobs:AllocationRun</c>
/// configuration section.
/// </summary>
public sealed class AllocationRunOptions
{
    public const string SectionName = "BackgroundJobs:AllocationRun";

    /// <summary>Enables or disables the scheduled run. Enabled by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>IANA time zone the cutoff is interpreted in. Defaults to Vietnam time.</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Daily cutoff in 24-hour "HH:mm" format. Defaults to 12:00 per the approved flow.</summary>
    public string CutoffLocalTime { get; set; } = "12:00";
}
