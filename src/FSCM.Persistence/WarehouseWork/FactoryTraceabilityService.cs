using System.Text.RegularExpressions;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using FSCM.Domain.Entities;

namespace FSCM.Persistence.WarehouseWork;

public sealed record SimulateProductionRequest(string FactoryCode, string ProductionLine, Guid ProductId,
    DateOnly ProductionDate, DateOnly ExpirationDate, int PalletCount, int CartonsPerPallet, int UnitsPerCarton);
public sealed record FactoryBatchView(FactoryProductionBatch Batch, string Sku, string ProductName, FactoryPallet[] Pallets, int CartonCount, int UnitCount);
public sealed record PalletManifest(FactoryPallet Pallet, FactoryProductionBatch Batch, string Sku, string ProductName, FactoryCarton[] Cartons);

public sealed record FactoryTraceEvent(string CartonId, string Code, string Kind, string Reason, string Status, Guid WarehouseId, DateTimeOffset? ScannedAt);
public sealed record FactoryCartonStock(string CartonId, Guid WarehouseId, string Status, Guid StockBatchId);
public sealed record FactoryTraceResult(FactoryProductionBatch Batch, string Sku, FactoryPallet? Pallet, FactoryCarton? Carton,
    FactoryProductUnit? Unit, FactoryProductUnit[] Units, FactoryTraceEvent[] History, FactoryCartonStock[] CurrentStock);

/// <summary>WMS reads external manifests through HTTP; it never writes the factory database.</summary>
public sealed class FactoryTraceabilityService(AppDbContext db, IFactoryGateway gateway)
{
    public static string BatchCode(string factory, DateOnly date, string line, int sequence) => $"BID-{factory}-{date:yyyyMMdd}-{line}-{sequence:D4}";
    public static string StockCode(string warehouse, string sku, string batch, DateOnly received, DateOnly expiry) =>
        $"{warehouse}-{sku}-{batch}-{received:yyyyMMdd}-{expiry:yyyyMMdd}";
    public static DateOnly VietnamDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(7)).DateTime);
    public static void Validate(SimulateProductionRequest data, DateOnly today)
    {
        if (!Regex.IsMatch(data.FactoryCode ?? "", "^[A-Z0-9]{1,12}$") || !new[] { "A", "B", "C", "D" }.Contains(data.ProductionLine))
            throw PickingOperationException.Validation("Mã nhà máy gồm 1–12 chữ in hoa/số; line là A, B, C hoặc D.");
        if (data.ProductionDate > today || data.ExpirationDate <= data.ProductionDate || data.ExpirationDate < today)
            throw PickingOperationException.Validation("NSX không ở tương lai; HSD phải sau NSX và chưa hết hạn.");
        if (data.PalletCount is < 1 or > 100 || data.CartonsPerPallet is < 1 or > 100 || data.UnitsPerCarton is < 1 or > 1000 ||
            (long)data.PalletCount * data.CartonsPerPallet > 9999 || (long)data.PalletCount * data.CartonsPerPallet * data.UnitsPerCarton > 50000)
            throw PickingOperationException.Validation("Mô phỏng tối đa 100 pallet, 100 carton/pallet và 50.000 sản phẩm/mẻ; tối đa 9.999 carton/mẻ.");
    }

    public Task<PalletManifest> ManifestAsync(string code, CancellationToken ct) => gateway.ManifestAsync(code, ct);
    public Task<FactoryInboundPallet[]> DispatchedAsync(Guid[] warehouseIds, CancellationToken ct) => gateway.DispatchedAsync(warehouseIds, ct);
    public Task<Guid> AcknowledgeReceiptAsync(FactoryReceiptSync receipt, CancellationToken ct) => gateway.AcknowledgeReceiptAsync(receipt, ct);
    public async Task<FactoryTraceResult> TraceAsync(string code, Guid actor, string role, CancellationToken ct)
    {
        var source = await gateway.TraceAsync(code.Trim(), ct);
        var globalScope = role is SystemRoles.Admin or SystemRoles.Operator;
        var allowed = await ReportingScopeQueries.ManagedWarehouses(db, actor).Select(x => x.Id).ToArrayAsync(ct);
        var codes = source.CartonCodes;
        if (!globalScope && !source.DestinationWarehouseIds.Any(allowed.Contains) &&
            !await db.BatchQrLabelRecords.AnyAsync(l => codes.Contains(l.QrCode) && allowed.Contains(l.CurrentWarehouseId), ct))
            throw PickingOperationException.Forbidden("Mã không thuộc phạm vi kho được giao.");
        var history = await (from c in db.WarehouseDocumentCartons.AsNoTracking()
            join d in db.WarehouseDocuments.AsNoTracking() on c.DocumentId equals d.Id
            where codes.Contains(c.CartonId) && (globalScope || allowed.Contains(d.WarehouseId))
            orderby c.ScannedAt
            select new FactoryTraceEvent(c.CartonId, d.Code, d.Kind, d.Reason, d.Status, d.WarehouseId, c.ScannedAt)).Take(2000).ToArrayAsync(ct);
        var current = await db.BatchQrLabelRecords.AsNoTracking().Where(x => codes.Contains(x.QrCode) &&
            (globalScope || allowed.Contains(x.CurrentWarehouseId)))
            .Select(x => new FactoryCartonStock(x.QrCode, x.CurrentWarehouseId, x.Status, x.BatchId)).ToArrayAsync(ct);
        return new(source.Batch, source.Sku, source.Pallet, source.Carton, source.Unit, source.Units, history, current);
    }
}
