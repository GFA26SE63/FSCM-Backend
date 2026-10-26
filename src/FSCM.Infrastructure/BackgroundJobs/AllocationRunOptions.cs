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

    /// <summary>
    /// Daily cutoff in 24-hour "HH:mm" format. Defaults to 00:00: the day closes at midnight and
    /// the run allocates what came in during the day that just ended.
    ///
    /// Only a fallback for a deployment with no published schedule - the schedule an Admin
    /// publishes wins, so that the ETA, the portal and this job cannot disagree about the hour.
    /// </summary>
    public string CutoffLocalTime { get; set; } = "00:00";
}
