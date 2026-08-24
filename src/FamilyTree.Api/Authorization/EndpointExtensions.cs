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

    /// <summary>
    /// Satisfied by any one of the given permissions.
    ///
    /// For a resource two pages legitimately reach through different permissions: the branch and
    /// generation lists are filter reference data for both the Members page (Member.View) and the
    /// Family Tree page (FamilyTree.View), and they expose only names each caller can already see
    /// in its own list. Guarding them with one of the two left a custom single-permission role
    /// staring at permanently empty dropdowns with no error to explain them.
    ///
    /// Built inline rather than registered by name: the named policies are one per permission
    /// code, and a combination has no code of its own.
    /// </summary>
    public static RouteHandlerBuilder RequireAnyPermission(
        this RouteHandlerBuilder builder, params string[] permissions) =>
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions)));
}
