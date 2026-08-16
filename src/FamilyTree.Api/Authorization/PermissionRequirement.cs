using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
