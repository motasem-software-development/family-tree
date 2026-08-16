using FamilyTree.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;

namespace FamilyTree.Api.Authorization;

/// <summary>
/// Evaluates permission claims carried by the access token. One handler serves every
/// permission — adding a capability means adding a constant and a seed row, never a new handler.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var granted = context.User.FindAll(JwtTokenService.PermissionClaim)
            .Any(c => c.Value == requirement.Permission);

        if (granted) context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
