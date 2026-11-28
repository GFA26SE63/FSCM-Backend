using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class WarehouseDocumentListTests
{
    [ConfiguredDatabaseFact]
    public async Task List_preserves_document_boundaries_and_matches_detail_for_shared_carton_history()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        // Read-only check; list/detail hydration does not invoke command dependencies or the factory API.
        var service = new WarehouseWorkService(db, null!, null!, null!, null!, TimeProvider.System, null!);
        var rows = await service.ListAsync(Guid.Empty, SystemRoles.Admin, default);
        Assert.Equal(await db.WarehouseDocuments.CountAsync(), rows.Length);
        Assert.Equal(await db.WarehouseDocumentLines.CountAsync(), rows.Sum(r => r.Lines.Count));
        Assert.Equal(await db.WarehouseDocumentCartons.CountAsync(), rows.Sum(r => r.Cartons.Count));
        Assert.All(rows, row =>
        {
            Assert.All(row.Lines, line => Assert.Equal(row.Document.Id, line.DocumentId));
            Assert.All(row.Cartons, carton => Assert.Equal(row.Document.Id, carton.DocumentId));
            Assert.All(row.Pallets!, mapping => Assert.Contains(row.Cartons, carton => carton.CartonId == mapping.CartonCode));
        });
        foreach (var row in rows.Where(r => r.Cartons.Count > 0).Take(3))
        {
            var detail = await service.GetAsync(row.Document.Id, Guid.Empty, SystemRoles.Admin, default);
            Assert.Equal(detail.Cartons.Select(c => c.Id).Order(), row.Cartons.Select(c => c.Id).Order());
            Assert.Equal(detail.Pallets!.OrderBy(p => p.CartonCode), row.Pallets!.OrderBy(p => p.CartonCode));
            Assert.Equal(detail.TransportStatus, row.TransportStatus);
        }
    }
}
