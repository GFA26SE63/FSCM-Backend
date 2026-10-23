using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Orders;

namespace FSCM.Application.Features.MobileSnapshots;

public sealed class MobileSnapshotService(IMobileSnapshotStore store, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MobileSnapshotResult> GetAsync(
        Guid salesUserId, string? sinceVersion, CancellationToken cancellationToken)
    {
        DateTimeOffset? changedAfter = null;
        if (!string.IsNullOrWhiteSpace(sinceVersion))
        {
            if (!long.TryParse(sinceVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < 0)
                throw OrderOperationException.Validation(
                    "sinceVersion must be an opaque version returned by this endpoint.");
            changedAfter = new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        var generatedAt = timeProvider.GetUtcNow();
        var full = await store.LoadAsync(salesUserId, null, cancellationToken);
        var currentVersionAt = full.LatestChangedAt;
        var version = currentVersionAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
        var checksumPayload = JsonSerializer.Serialize(new
        {
            version,
            full.Retailers,
            full.Categories,
            full.Products,
            full.Prices,
            full.Promotions,
            full.Mlors,
            full.IndicativeStock,
            full.DefaultMlor
        }, JsonOptions);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checksumPayload)));
        var notModified = changedAfter.HasValue && changedAfter.Value >= currentVersionAt;
        var data = !changedAfter.HasValue ? full : notModified
            ? new MobileSnapshotData([], [], [], [], [], [], [], null, currentVersionAt)
            : await store.LoadAsync(salesUserId, changedAfter, cancellationToken);

        return new(version, checksum, changedAfter.HasValue ? "Delta" : "Full", notModified,
            generatedAt, changedAfter, data.Retailers, data.Categories, data.Products, data.Prices,
            data.Promotions, data.Mlors, data.IndicativeStock, data.DefaultMlor,
            "Tồn kho chỉ để tham khảo, không phải hàng đã giữ và không dùng để từ chối gửi đơn.",
            "Giá là giá cơ sở dự kiến; giảm theo HSD chỉ được chốt sau khi phân bổ chọn batch.");
    }
}
