using System.Text.Json;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

/// <summary>Result of one pallet on a factory receipt: carton counts by inspection outcome.</summary>
public sealed record FactoryReceiptGroupPallet(string PalletCode, Guid DocumentId, int Accepted, int Damaged, int Missing);

/// <summary>
/// Display grouping for "Lịch sử Phiếu Nhập Kho": the factory receipts (PNK) that belong to one dispatch note (PXN).
/// Receipts opened from a dispatch note are already one per note; older receipts were opened one per pallet, so several
/// of them can belong to the same note. Documents are not changed: each PNK keeps its own code, ledger and QR history.
/// </summary>
public sealed record FactoryReceiptGroup(string Key, string? DispatchCode, DateTimeOffset? DispatchedAt,
    Guid[] DocumentIds, FactoryReceiptGroupPallet[] Pallets);

/// <summary>Input row of <see cref="WarehouseWorkService.GroupFactoryReceipts"/>: one factory receipt document.</summary>
public sealed record FactoryReceiptSource(Guid DocumentId, DateTimeOffset CreatedAt, Guid WarehouseId,
    Guid? DispatchNoteId, string? DispatchCode);

/// <summary>Input row: a pallet received on a document (local snapshot) and the note it left the factory on, if known.</summary>
public sealed record FactoryReceivedPalletSource(Guid PalletId, string PalletCode, Guid DocumentId, DateTimeOffset? DispatchedAt,
    string? InspectionJson, Guid? DispatchNoteId, string? DispatchCode);

public sealed partial class WarehouseWorkService
{
    /// <summary>
    /// Groups factory receipts by dispatch note. A document's note is its own (dispatch-note PNK) or the note of its
    /// pallets (Traceability). When the note is unknown, pallets that left together (same warehouse and dispatch instant)
    /// still form one group; a receipt without any received pallet stays on its own.
    /// </summary>
    public static FactoryReceiptGroup[] GroupFactoryReceipts(IReadOnlyCollection<FactoryReceiptSource> documents,
        IReadOnlyCollection<FactoryReceivedPalletSource> pallets)
    {
        var palletsByDoc = pallets.ToLookup(p => p.DocumentId);
        string KeyOf(FactoryReceiptSource d)
        {
            if (d.DispatchNoteId is Guid own) return $"note:{own:N}";
            var mine = palletsByDoc[d.DocumentId].ToArray();
            if (mine.FirstOrDefault(p => p.DispatchNoteId.HasValue) is { DispatchNoteId: Guid note }) return $"note:{note:N}";
            if (mine.FirstOrDefault(p => p.DispatchedAt.HasValue) is { DispatchedAt: DateTimeOffset at })
                return $"trip:{d.WarehouseId:N}:{at.UtcTicks}";
            return $"doc:{d.DocumentId:N}";
        }
        static (int Accepted, int Damaged, int Missing) Count(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return (0, 0, 0);
            var rows = ReceiptInspection.Read(json);
            return (rows.Count(r => r.Disposition == "Accepted"), rows.Count(r => r.Disposition == "Damaged"), rows.Count(r => r.Disposition == "Missing"));
        }
        return documents.GroupBy(KeyOf).Select(g =>
        {
            var docs = g.OrderBy(d => d.CreatedAt).ToArray();
            var groupPallets = docs.SelectMany(d => palletsByDoc[d.DocumentId]).OrderBy(p => p.PalletCode, StringComparer.Ordinal).ToArray();
            var code = docs.Select(d => d.DispatchCode).Concat(groupPallets.Select(p => p.DispatchCode))
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            return new FactoryReceiptGroup(g.Key, code, groupPallets.Min(p => p.DispatchedAt),
                docs.Select(d => d.DocumentId).ToArray(),
                groupPallets.Select(p => { var c = Count(p.InspectionJson); return new FactoryReceiptGroupPallet(p.PalletCode, p.DocumentId, c.Accepted, c.Damaged, c.Missing); }).ToArray());
        }).OrderByDescending(g => docsCreated(g)).ToArray();

        DateTimeOffset docsCreated(FactoryReceiptGroup g) => documents.Where(d => g.DocumentIds.Contains(d.DocumentId)).Max(d => d.CreatedAt);
    }

    /// <summary>Factory receipts in the caller's scope, grouped by dispatch note for the receipt history.</summary>
    public async Task<FactoryReceiptGroup[]> FactoryReceiptGroupsAsync(Guid actor, string role, CancellationToken ct)
    {
        var q = Documents.AsNoTracking().Where(d => d.Kind == "Receipt" && d.Reason == "Factory");
        if (role is not (SystemRoles.Admin or SystemRoles.Operator))
            q = q.Where(x => ReportingScopeQueries.ManagedWarehouses(db, actor).Any(w => w.Id == x.WarehouseId));
        var docs = await q.Select(d => new { d.Id, d.CreatedAt, d.WarehouseId, d.OperationKey }).ToArrayAsync(ct);
        if (docs.Length == 0) return [];
        var ids = docs.Select(d => d.Id).ToArray();
        var expectations = (await db.Set<AuditLog>().AsNoTracking()
                .Where(e => e.EntityName == "WarehouseDocument" && e.Action == ExpectedPalletsAction && ids.Contains(e.EntityId))
                .Select(e => new { e.EntityId, e.NewValues }).ToArrayAsync(ct))
            .GroupBy(e => e.EntityId)
            .ToDictionary(g => g.Key, g => g.First().NewValues is { } json ? JsonSerializer.Deserialize<FactoryReceiptExpectation>(json) : null);
        var received = await db.Set<FactoryPallet>().AsNoTracking()
            .Where(p => p.ReceiptDocumentId.HasValue && ids.Contains(p.ReceiptDocumentId.Value) && p.Status == "Received")
            .Select(p => new { p.Id, p.Code, DocumentId = p.ReceiptDocumentId!.Value, p.DispatchedAt, p.ReceiptInspectionJson })
            .ToArrayAsync(ct);
        // Older per-pallet receipts: ask Traceability which note each pallet left on. Without it (service down),
        // pallets that left together are still grouped by their dispatch instant.
        var legacyPalletIds = received.Where(p => !expectations.ContainsKey(p.DocumentId)).Select(p => p.Id).Distinct().ToArray();
        var notes = new Dictionary<Guid, FactoryPalletDispatchInfo>();
        try
        {
            foreach (var chunk in legacyPalletIds.Chunk(500))
                foreach (var info in await factory.PalletDispatchInfoAsync(chunk, ct)) notes[info.PalletId] = info;
        }
        catch (PickingOperationException) { }
        var sources = docs.Select(d =>
        {
            var e = expectations.GetValueOrDefault(d.Id);
            return new FactoryReceiptSource(d.Id, d.CreatedAt, d.WarehouseId, e?.DispatchNoteId, e?.DispatchCode);
        }).ToArray();
        var palletRows = received.Select(p =>
        {
            var info = notes.GetValueOrDefault(p.Id);
            return new FactoryReceivedPalletSource(p.Id, p.Code, p.DocumentId, p.DispatchedAt, p.ReceiptInspectionJson,
                info?.DispatchNoteId, info?.DispatchCode);
        }).ToArray();
        return GroupFactoryReceipts(sources, palletRows);
    }
}
