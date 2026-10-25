using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FSCM.Application.Features.MobileSnapshots;

public sealed class MobileSnapshotService(IMobileSnapshotStore store, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MobileSnapshotResult> GetAsync(
        Guid salesUserId, CancellationToken cancellationToken)
    {
        var generatedAt = timeProvider.GetUtcNow();
        var full = await store.LoadAsync(salesUserId, generatedAt, cancellationToken);
        var checksumPayload = JsonSerializer.Serialize(new
        {
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
        var version = $"f29-v1-{checksum}";

        return new(version, checksum, "Full", generatedAt, full.Retailers, full.Categories,
            full.Products, full.Prices, full.Promotions, full.Mlors, full.IndicativeStock, full.DefaultMlor,
            "Tồn kho chỉ để tham khảo, không phải hàng đã giữ và không dùng để từ chối gửi đơn.",
            "Giá là giá cơ sở dự kiến; giảm theo HSD chỉ được chốt sau khi phân bổ chọn batch.");
    }
}
