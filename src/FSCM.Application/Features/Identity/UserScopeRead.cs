namespace FSCM.Application.Features.Identity;

public sealed record UserScopeRead(Guid UserId, IdentityScope Scope);
