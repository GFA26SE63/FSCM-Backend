namespace FSCM.Domain.Entities;

public sealed class RolePermission
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public RoleDefinition? Role { get; set; }
}
