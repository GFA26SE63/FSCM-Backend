using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Partners.Domain.Retailers;

public enum RegistrationStatus { Pending, Approved, Rejected }

/// <summary>"Khai báo điểm bán mới" from the Sales app → request KB-0000, approved by Admin into a Retailer.</summary>
public sealed class RetailerRegistration : AggregateRoot<string>
{
    private RetailerRegistration() { }
    public RetailerRegistration(string code, string declaredByUserId, string declaredByName, string name, string phone, string address,
        double lat, double lng, string areaCode, string kind, RetailerPotential potential, DateTime nowUtc)
    {
        Guard.Against(string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone), "registration.fields", "Tên và số điện thoại bắt buộc.");
        Id = code; DeclaredByUserId = declaredByUserId; DeclaredByName = declaredByName; Name = name; Phone = phone; Address = address;
        Lat = lat; Lng = lng; AreaCode = areaCode; Kind = kind; Potential = potential; Status = RegistrationStatus.Pending; CreatedAtUtc = nowUtc;
    }
    public string DeclaredByUserId { get; private set; } = "";
    public string DeclaredByName { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string Address { get; private set; } = "";
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public string AreaCode { get; private set; } = "";
    public string Kind { get; private set; } = "";
    public RetailerPotential Potential { get; private set; } = RetailerPotential.Unknown;
    public RegistrationStatus Status { get; private set; }
    public string? RetailerCode { get; private set; }
    public string? DecidedBy { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    public Retailer Approve(string retailerCode, string by, int creditTermDays, DateTime nowUtc)
    {
        Guard.Against(Status != RegistrationStatus.Pending, "registration.state", $"{Id} đã được xử lý.");
        var r = new Retailer(retailerCode, Name, Phone, Address, Lat, Lng, AreaCode, Kind, Potential, $"Sales {DeclaredByName} khai báo ({Id})", nowUtc);
        r.SetCreditTerm(creditTermDays, nowUtc);
        Status = RegistrationStatus.Approved; RetailerCode = retailerCode; DecidedBy = by; DecidedAtUtc = nowUtc;
        return r;
    }

    public void Reject(string by, string reason, DateTime nowUtc)
    {
        Guard.Against(Status != RegistrationStatus.Pending, "registration.state", $"{Id} đã được xử lý.");
        Guard.Against(string.IsNullOrWhiteSpace(reason), "registration.reason", "Nhập lý do từ chối.");
        Status = RegistrationStatus.Rejected; RejectReason = reason; DecidedBy = by; DecidedAtUtc = nowUtc;
    }
}
