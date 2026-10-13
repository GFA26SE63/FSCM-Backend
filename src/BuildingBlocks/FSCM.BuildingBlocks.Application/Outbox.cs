namespace FSCM.BuildingBlocks.Application;

/// <summary>
/// Transactional outbox seen from the application layer. Events enqueued here are written to the module's
/// outbox table in the same SaveChanges as the aggregate change, then dispatched by the worker.
/// Every module unit of work implements it (ModuleDbContext).
/// </summary>
public interface IEventOutbox { void Enqueue(IIntegrationEvent e); }

/// <summary>Role names used in [Authorize(Roles = ...)] attributes.</summary>
public static class RoleNames
{
    public const string Admin = nameof(Role.Admin);
    public const string Operator = nameof(Role.Operator);
    public const string SalesManager = nameof(Role.SalesManager);
    public const string WarehouseKeeper = nameof(Role.WarehouseKeeper);
    public const string Sales = nameof(Role.Sales);
    public const string Retailer = nameof(Role.Retailer);
    public const string Staff = "Admin,Operator";
}

/// <summary>Used by the worker / jobs where there is no HTTP user.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public string UserId => "system";
    public string FullName => "Hệ thống";
    public Role Role => Role.Admin;
    public string? WarehouseCode => null;
    public string? SalesGroupCode => null;
    public string? RetailerCode => null;
}

public static class Paging
{
    public static PagedResult<T> Page<T>(this IEnumerable<T> source, PageRequest p)
    {
        var list = source as IReadOnlyList<T> ?? source.ToList();
        var size = Math.Clamp(p.Size, 1, 200); var page = Math.Max(1, p.Page);
        return new PagedResult<T>(list.Skip((page - 1) * size).Take(size).ToList(), list.Count, page, size);
    }
}
