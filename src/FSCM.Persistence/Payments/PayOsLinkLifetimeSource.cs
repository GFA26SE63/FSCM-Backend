using FSCM.Application.Features.Payments;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Payments;

public sealed class PayOsLinkLifetimeSource(AppDbContext db) : IPayOsLinkLifetimeSource
{
    public async Task<int> GetMinutesAsync(CancellationToken cancellationToken)
    {
        var raw = await db.SystemConfigurationRecords.AsNoTracking()
            .Where(x => x.ConfigKey == BusinessConfigurationKeys.PayOsLinkLifetimeMinutes && x.IsActive)
            .OrderByDescending(x => x.Version)
            .Select(x => x.ConfigValue)
            .FirstOrDefaultAsync(cancellationToken);
        // Unset or unreadable keeps the previous behaviour: no FSCM-imposed link deadline.
        return int.TryParse(raw, out var minutes) &&
            minutes is > 0 and <= BusinessConfigurationKeys.MaxPayOsLinkLifetimeMinutes ? minutes : 0;
    }
}
