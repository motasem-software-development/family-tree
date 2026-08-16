using FamilyTree.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

public static class EndpointExtensions
{
    /// <summary>Registers one policy per permission code, named after the code itself.</summary>
    public static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var permission in Permissions.All)
            builder.AddPolicy(permission, policy => policy.AddRequirements(new PermissionRequirement(permission)));

        return builder;
    }

    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.RequireAuthorization(permission);
}
