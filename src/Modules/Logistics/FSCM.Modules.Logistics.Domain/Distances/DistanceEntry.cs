using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Logistics.Domain.Distances;

public enum PointType { Warehouse, Retailer }

/// <summary>
/// One shared distance table for warehouse↔retailer and warehouse↔warehouse.
/// Road distance computed with Dijkstra on the road graph, recomputed when coordinates change.
/// </summary>
public sealed class DistanceEntry : Entity<Guid>
{
    private DistanceEntry() { }
    public DistanceEntry(string fromWarehouse, string toPoint, PointType toType, decimal km, string method)
    { Id = Guid.NewGuid(); FromWarehouse = fromWarehouse; ToPoint = toPoint; ToType = toType; Km = km; Method = method; ComputedAtUtc = DateTime.UtcNow; }
    public string FromWarehouse { get; private set; } = "";
    public string ToPoint { get; private set; } = "";
    public PointType ToType { get; private set; }
    public decimal Km { get; private set; }
    public string Method { get; private set; } = "dijkstra-road";
    public DateTime ComputedAtUtc { get; private set; }
    public void Recompute(decimal km) { Km = km; ComputedAtUtc = DateTime.UtcNow; }
}
