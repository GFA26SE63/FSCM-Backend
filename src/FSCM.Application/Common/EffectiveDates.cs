namespace FSCM.Application.Common;

/// <summary>Business date boundaries are always Vietnam dates, independent of server time zone.</summary>
public static class EffectiveDates
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static DateOnly VietnamDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToOffset(VietnamOffset).DateTime);

    public static DateTimeOffset Tomorrow(DateTimeOffset now) =>
        new(VietnamDate(now).AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset);

    public static bool IsBeforeToday(DateTimeOffset start, DateTimeOffset now) =>
        VietnamDate(start) < VietnamDate(now);

    public static DateTimeOffset StartFromNow(DateTimeOffset start, DateTimeOffset now) =>
        (start < now ? now : start).ToUniversalTime();
}
