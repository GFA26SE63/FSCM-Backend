using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Inventory.Domain.Warehouses;

public enum WarehouseType { Central, Regional }

public sealed class Warehouse : AggregateRoot<string>
{
    private Warehouse() { }
    public Warehouse(string code, string name, WarehouseType type, string address, double lat, double lng)
    {
        Guard.Against(!IdFormat.IsWarehouseCode(code), "warehouse.code", "Mã kho dạng WH-XX.");
        Id = code; Name = name; Type = type; Address = address; Lat = lat; Lng = lng; Active = true;
    }
    public string Name { get; private set; } = "";
    public WarehouseType Type { get; private set; }
    public string Address { get; private set; } = "";
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public string? KeeperUserId { get; private set; }
    public int CapacityCases { get; private set; }
    public bool Active { get; private set; }
    public void AssignKeeper(string? userId) => KeeperUserId = userId;
    public void Update(string name, string address, double lat, double lng, int capacityCases, bool active)
    {
        Guard.Against(string.IsNullOrWhiteSpace(name), "warehouse.name", "Tên kho bắt buộc.");
        Name = name.Trim(); Address = address; Lat = lat; Lng = lng; CapacityCases = capacityCases; Active = active;
    }
}
