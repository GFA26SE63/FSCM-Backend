using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Logistics.Domain.Fleet;

public enum FleetStatus { Active, Maintenance, OnLeave }

/// <summary>Vehicle master data; the number plate is the code. Chosen from a dropdown when an order goes to "Đang giao".</summary>
public sealed class Vehicle : AggregateRoot<string>
{
    private Vehicle() { }
    public Vehicle(string plate, string type, int capacityCases, string warehouseCode)
    {
        Guard.Against(!IdFormat.IsVehiclePlate(plate), "vehicle.plate", "Biển số dạng 51C-482.17.");
        Id = plate.Replace(" ", ""); Type = type; CapacityCases = capacityCases; WarehouseCode = warehouseCode; Status = FleetStatus.Active;
    }
    public string Type { get; private set; } = "";
    public int CapacityCases { get; private set; }
    public string WarehouseCode { get; private set; } = "";
    public FleetStatus Status { get; private set; }
    public void Update(string type, int capacity, string warehouseCode, FleetStatus status) { Type = type; CapacityCases = capacity; WarehouseCode = warehouseCode; Status = status; }
    public void EnsureCanCarry(int cases)
    {
        Guard.Against(Status != FleetStatus.Active, "vehicle.inactive", $"Xe {Id} đang không hoạt động.");
        Guard.Against(cases > CapacityCases, "vehicle.capacity", $"Xe {Id} chở tối đa {CapacityCases} thùng, đơn có {cases} thùng.");
    }
}

/// <summary>Driver master data (NG-000). Drivers have no app account.</summary>
public sealed class Driver : AggregateRoot<string>
{
    private Driver() { }
    public Driver(string code, string name, string phone, string licenseClass) { Id = code; Name = name; Phone = phone; LicenseClass = licenseClass; Status = FleetStatus.Active; }
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string LicenseClass { get; private set; } = "C";
    public FleetStatus Status { get; private set; }
    public void Update(string name, string phone, string license, FleetStatus status) { Name = name; Phone = phone; LicenseClass = license; Status = status; }
}
