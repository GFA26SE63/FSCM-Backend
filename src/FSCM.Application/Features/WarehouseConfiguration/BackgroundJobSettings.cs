using System.Text.Json;

namespace FSCM.Application.Features.WarehouseConfiguration;

public sealed record AllocationRecoverySettings(bool Enabled);
public sealed record DateTrackerSettings(bool Enabled, string TimeZone, string RunAtLocalTime);
public sealed record InventoryOutboxSettings(bool Enabled, int PollIntervalSeconds, int BatchSize);
public sealed record OrderDeadlineSettings(bool Enabled, int PollIntervalMinutes, int PrepaidPaymentWindowDays);

/// <summary>Read per sweep so database changes also reach disabled workers without restarting.</summary>
public sealed class BackgroundJobSettings(IWarehouseConfigurationStore store)
{
    public const string AllocationRecoveryKey = "jobs.allocation_recovery";
    public const string DateTrackerKey = "jobs.date_tracker";
    public const string InventoryOutboxKey = "jobs.inventory_outbox";
    public const string OrderDeadlineKey = "jobs.order_deadline";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> ReadAsync<T>(string key, CancellationToken ct)
    {
        var row = await store.FindCurrentConfigurationAsync(key, ct)
            ?? throw new InvalidOperationException($"Required database setting '{key}' is missing.");
        return JsonSerializer.Deserialize<T>(row.ConfigValue, JsonOptions)
            ?? throw new InvalidOperationException($"Database setting '{key}' is invalid.");
    }

    public static void Validate(string key, JsonElement value)
    {
        try
        {
            switch (key)
            {
                case AllocationRecoveryKey:
                    _ = value.Deserialize<AllocationRecoverySettings>(JsonOptions);
                    break;
                case DateTrackerKey:
                    var date = value.Deserialize<DateTrackerSettings>(JsonOptions)!;
                    if (!TimeOnly.TryParseExact(date.RunAtLocalTime, "HH:mm", out _)) throw new FormatException();
                    TimeZoneInfo.FindSystemTimeZoneById(date.TimeZone);
                    break;
                case InventoryOutboxKey:
                    var inventory = value.Deserialize<InventoryOutboxSettings>(JsonOptions)!;
                    if (inventory.PollIntervalSeconds is < 1 or > 300 || inventory.BatchSize is < 1 or > 500)
                        throw new FormatException();
                    break;
                case OrderDeadlineKey:
                    var order = value.Deserialize<OrderDeadlineSettings>(JsonOptions)!;
                    if (order.PollIntervalMinutes is < 1 or > 60 || order.PrepaidPaymentWindowDays is < 1 or > 30)
                        throw new FormatException();
                    break;
            }
            var properties = key switch
            {
                AllocationRecoveryKey => new[] { "enabled" },
                DateTrackerKey => new[] { "enabled", "timeZone", "runAtLocalTime" },
                InventoryOutboxKey => new[] { "enabled", "pollIntervalSeconds", "batchSize" },
                _ => new[] { "enabled", "pollIntervalMinutes", "prepaidPaymentWindowDays" }
            };
            if (value.ValueKind != JsonValueKind.Object || properties.Any(p => !value.TryGetProperty(p, out _)) ||
                value.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new FormatException();
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or
            TimeZoneNotFoundException or InvalidTimeZoneException or NullReferenceException or InvalidOperationException)
        {
            throw WarehouseConfigurationOperationException.Validation("Invalid background job settings. Check all required fields, time zone, time and numeric ranges.");
        }
    }
}
