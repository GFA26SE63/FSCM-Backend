using FSCM.Application.Features.Identity;

namespace FSCM.Application.Features.EntityQueries;

/// <summary>
/// Provides composable, read-only entity queries without exposing a persistence
/// implementation to the API layer.
/// </summary>
public interface IEntityQueryService
{
    IQueryable<TEntity> Query<TEntity>(EntityQueryAccess access) where TEntity : class;
}

public sealed record EntityQueryAccess(Guid UserId, string Role, IdentityScope Scope);
