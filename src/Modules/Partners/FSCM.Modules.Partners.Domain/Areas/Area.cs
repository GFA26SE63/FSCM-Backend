using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Partners.Domain.Areas;

/// <summary>Sales area = abbreviation (Q12, GV, HM).</summary>
public sealed class Area : AggregateRoot<string>
{
    private Area() { }
    public Area(string code, string name)
    {
        Guard.Against(!IdFormat.IsArea(code), "area.code", "Mã khu vực là chữ viết tắt 2–4 ký tự (Q12).");
        Id = code; Name = name; Active = true;
    }
    public string Name { get; private set; } = "";
    public bool Active { get; private set; }
    public void Update(string name, bool active) { Name = name; Active = active; }
}

public sealed class SalesMember
{
    private SalesMember() { }
    public SalesMember(string userId, string fullName) { UserId = userId; FullName = fullName; }
    public string UserId { get; private set; } = "";
    public string FullName { get; private set; } = "";
}

/// <summary>Sales group {area}-{letter} (Q12-A). A Sales belongs to one group and sees only retailers of the group's area.</summary>
public sealed class SalesGroup : AggregateRoot<string>
{
    private readonly List<SalesMember> _members = new();
    private SalesGroup() { }
    public SalesGroup(string code, string areaCode, string name, string? managerUserId)
    {
        Guard.Against(!IdFormat.IsAreaGroup(code) || !code.StartsWith(areaCode + "-", StringComparison.Ordinal), "group.code", "Mã nhóm dạng {khu vực}-{chữ cái}, ví dụ Q12-A.");
        Id = code; AreaCode = areaCode; Name = name; ManagerUserId = managerUserId;
    }
    public string AreaCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? ManagerUserId { get; private set; }
    public IReadOnlyCollection<SalesMember> Members => _members.AsReadOnly();

    /// <summary>Replaces the member list; returns removed user ids.</summary>
    public IReadOnlyList<string> SetMembers(IEnumerable<SalesMember> members)
    {
        var list = members.GroupBy(m => m.UserId).Select(g => g.First()).ToList();
        var removed = _members.Where(m => list.All(n => n.UserId != m.UserId)).Select(m => m.UserId).ToList();
        _members.Clear(); _members.AddRange(list);
        return removed;
    }
    public bool HasMember(string userId) => _members.Any(m => m.UserId == userId);
    public void Rename(string name, string? managerUserId) { Name = name; ManagerUserId = managerUserId; }
}
