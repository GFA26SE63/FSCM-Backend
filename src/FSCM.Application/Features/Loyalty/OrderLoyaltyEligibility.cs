using System.Text.Json;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed record CapturedLoyaltyProgram(Guid Id, string Code, decimal SpendAmountPerPoint);

/// <summary>Immutable eligibility stored in the order's existing OrderCaptured JSON audit.</summary>
public sealed record OrderLoyaltyEligibility(int Version, Guid RetailerId, DateTimeOffset CapturedAt,
    IReadOnlyCollection<CapturedLoyaltyProgram> Programs)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OrderLoyaltyEligibility Capture(Guid retailerId, DateTimeOffset at, IEnumerable<LoyaltyProgram> programs) =>
        new(1, retailerId, at, programs.OrderBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new CapturedLoyaltyProgram(p.Id, p.Code, p.SpendAmountPerPoint)).ToArray());

    public LoyaltyProgram? BestForAmount(decimal amount) => LoyaltyProgramSelection.BestForAmount(
        Programs.Select(p => new LoyaltyProgram { Id = p.Id, Code = p.Code, SpendAmountPerPoint = p.SpendAmountPerPoint }), amount);

    /// <summary>Only absent legacy snapshots return null; malformed snapshots must not fall back to today's rules.</summary>
    public static OrderLoyaltyEligibility? Read(string? orderAuditJson, Guid retailerId, DateTimeOffset receivedAt)
    {
        if (orderAuditJson is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(orderAuditJson);
            if (!doc.RootElement.TryGetProperty("loyaltyEligibility", out var json)) return null;
            var snapshot = json.Deserialize<OrderLoyaltyEligibility>(JsonOptions);
            if (snapshot is null || snapshot.Version != 1 || snapshot.RetailerId != retailerId ||
                (snapshot.CapturedAt - receivedAt).Duration() > TimeSpan.FromMilliseconds(1) || snapshot.Programs is null ||
                snapshot.Programs.Any(p => p is null || p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Code) || p.SpendAmountPerPoint <= 0) ||
                snapshot.Programs.Select(p => p.Id).Distinct().Count() != snapshot.Programs.Count)
                throw Invalid();
            return snapshot;
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
    }

    private static LoyaltyOperationException Invalid() =>
        LoyaltyOperationException.Conflict("Snapshot quyền tích điểm của đơn không hợp lệ; cần đối soát trước khi cộng điểm.");
}
