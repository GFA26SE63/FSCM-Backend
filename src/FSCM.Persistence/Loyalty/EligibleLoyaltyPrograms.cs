using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;

namespace FSCM.Persistence.Loyalty;

internal static class EligibleLoyaltyPrograms
{
    public static IQueryable<LoyaltyProgram> ForRetailer(AppDbContext db, Guid retailerId, DateTimeOffset now) =>
        db.LoyaltyProgramRecords.Where(program => program.IsActive && program.StartAt <= now && program.EndAt >= now &&
            db.LoyaltyProgramRetailerRecords.Any(link => link.RetailerId == retailerId && link.LoyaltyProgramId == program.Id));
}
