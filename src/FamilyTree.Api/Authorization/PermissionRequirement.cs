using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

/// <summary>
/// Holds the permissions that satisfy an endpoint. One is the normal case; several mean "any of
/// these", for a resource two pages reach through different permissions — see
/// <c>EndpointExtensions.RequireAnyPermission</c>.
/// </summary>
public sealed class PermissionRequirement(params string[] permissions) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> Permissions { get; } = permissions;
}
