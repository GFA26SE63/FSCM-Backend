namespace FSCM.Domain.Entities;

public sealed class UserRegionAssignment
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public Guid RegionId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? UnassignedAt { get; set; }
    public bool IsActive { get; set; }
}
