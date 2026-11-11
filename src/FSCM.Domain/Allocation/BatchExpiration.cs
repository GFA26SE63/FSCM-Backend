namespace FSCM.Domain.Allocation;

/// <summary>A date-only HSD remains usable until the end of that Vietnam calendar day.</summary>
public static class BatchExpiration
{
    public static DateTimeOffset ExclusiveEnd(DateOnly expirationDate) =>
        new DateTimeOffset(expirationDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7))
            .ToUniversalTime();
}
