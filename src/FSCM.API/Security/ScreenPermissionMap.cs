using FSCM.Domain.Authorization;

namespace FSCM.API.Security;

/// <summary>Uses the same screen dependencies as role grant persistence.</summary>
public static class ScreenPermissionMap
{
    public static string[]? Rest(string controller, string action, string? role) =>
        ScreenOperationDependencies.Rest(controller, action, role);
}
