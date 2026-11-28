using FSCM.Application.Features.Identity;

namespace FSCM.Application.Features.EntityQueries;

/// <summary>
/// Provides composable, read-only entity queries without exposing a persistence
/// implementation to the API layer.
/// </summary>
public interface IEntityQueryService
{
    /// <summary>Scope and grants are applied first, then a unique primary-key ordering for paging.
    /// Callers may replace that default with an explicit sort, including a unique tie-breaker.</summary>
    IQueryable<TEntity> Query<TEntity>(EntityQueryAccess access) where TEntity : class;
}

public sealed record EntityQueryAccess(Guid UserId, string Role, IdentityScope Scope,
    IReadOnlyCollection<string>? Permissions = null);
