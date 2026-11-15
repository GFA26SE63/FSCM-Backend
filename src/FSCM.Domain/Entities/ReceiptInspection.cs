using System.Text.Json;

namespace FSCM.Domain.Entities;

public sealed record ReceiptCartonInspection(string CartonCode, string Disposition, string? Reason, string? QuarantineLocation);

public static class ReceiptInspection
{
    public static string Normalize(IEnumerable<string> expectedCodes, ReceiptCartonInspection[] rows)
    {
        var expected = expectedCodes.ToHashSet(StringComparer.Ordinal);
        if (rows is null || rows.Length != expected.Count || rows.Any(x => x is null) ||
            rows.Select(x => x.CartonCode).Distinct(StringComparer.Ordinal).Count() != rows.Length ||
            rows.Any(x => !expected.Contains(x.CartonCode)))
            throw new ArgumentException("Phải kiểm đếm đúng mỗi CID trong pallet một lần.");
        foreach (var row in rows)
        {
            if (row.Disposition is not ("Accepted" or "Damaged" or "Missing"))
                throw new ArgumentException("Kết quả kiểm đếm không hợp lệ.");
            if (row.Reason?.Length > 300 || row.QuarantineLocation?.Length > 100 ||
                (row.Disposition != "Accepted" && string.IsNullOrWhiteSpace(row.Reason)) ||
                (row.Disposition == "Damaged" && string.IsNullOrWhiteSpace(row.QuarantineLocation)))
                throw new ArgumentException("Hàng thiếu/hỏng cần lý do (tối đa 300 ký tự); hàng hỏng cần vị trí cách ly (tối đa 100 ký tự).");
        }
        return JsonSerializer.Serialize(rows.OrderBy(x => x.CartonCode, StringComparer.Ordinal).Select(x => x with {
            Reason = x.Disposition == "Accepted" ? null : x.Reason!.Trim(),
            QuarantineLocation = x.Disposition == "Damaged" ? x.QuarantineLocation!.Trim() : null
        }));
    }

    public static ReceiptCartonInspection[] Read(string json) =>
        JsonSerializer.Deserialize<ReceiptCartonInspection[]>(json) ?? throw new ArgumentException("Thiếu kết quả kiểm đếm.");
}
