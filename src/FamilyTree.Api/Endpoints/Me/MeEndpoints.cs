using System.Security.Claims;
using FamilyTree.Api.Authorization;
using FamilyTree.Application.Common;
using FamilyTree.Contracts.Auth;
using FamilyTree.Domain.Authorization;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Endpoints.Me;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", async (
            ITenantContext tenant,
            ApplicationDbContext context,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Filtered query: a tenant with no tree of its own simply finds nothing.
            var tree = await context.FamilyTrees.FirstOrDefaultAsync(ct);
            if (tree is null) return Results.NotFound();

            var email = http.User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

            var permissions = http.User.FindAll(JwtTokenService.PermissionClaim)
                .Select(c => c.Value)
                .ToArray();

            return Results.Ok(new CurrentUserResponse(
                tenant.UserId, email, tenant.TenantId, tree.Name, permissions));
        })
        .RequirePermission(Permissions.FamilyTree.View)
        .WithTags("Me");

        return app;
    }
}
