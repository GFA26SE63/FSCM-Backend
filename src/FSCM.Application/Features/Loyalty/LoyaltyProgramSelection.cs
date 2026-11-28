using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

/// <summary>One earning program per order; the wallet is shared between eligible programs.</summary>
public static class LoyaltyProgramSelection
{
    public static decimal PointsFor(decimal amount, decimal spendPerPoint) =>
        amount <= 0 || spendPerPoint <= 0 ? 0 : decimal.Floor(amount / spendPerPoint);

    public static LoyaltyProgram? BestForAmount(IEnumerable<LoyaltyProgram> eligiblePrograms, decimal amount) =>
        eligiblePrograms.Where(p => p.SpendAmountPerPoint > 0)
            .OrderByDescending(p => PointsFor(amount, p.SpendAmountPerPoint))
            .ThenBy(p => p.Code, StringComparer.Ordinal).ThenBy(p => p.Id).FirstOrDefault();
}
