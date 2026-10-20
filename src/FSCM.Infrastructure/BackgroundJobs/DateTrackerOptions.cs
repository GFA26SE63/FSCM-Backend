namespace FSCM.Infrastructure.BackgroundJobs;

/// <summary>
/// Configuration for the nightly batch date-tracker job. Bound from the
/// <c>BackgroundJobs:DateTracker</c> configuration section.
/// </summary>
public sealed class DateTrackerOptions
{
    public const string SectionName = "BackgroundJobs:DateTracker";

    /// <summary>Enables or disables the scheduled job. Enabled by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>IANA time zone the run time is interpreted in. Defaults to Vietnam time.</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Daily run time in 24-hour "HH:mm" format. Defaults to 01:30 per the Sprint 1 requirement.</summary>
    public string RunAtLocalTime { get; set; } = "01:30";
}
