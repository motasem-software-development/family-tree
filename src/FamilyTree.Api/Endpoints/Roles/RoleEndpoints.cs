using FamilyTree.Api.Authorization;
using FamilyTree.Application.Roles;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Roles;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles").WithTags("Roles");

        group.MapGet("/", async (IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.ListAsync(ct)))
            .RequirePermission(Permissions.Role.View);

        group.MapGet("/{id:guid}", async (Guid id, IRoleService roles, CancellationToken ct) =>
            await roles.GetAsync(id, ct) is { } role ? Results.Ok(role) : Results.NotFound())
            .RequirePermission(Permissions.Role.View);

        // Outside the /roles group: it is the catalog the role editor reads, not a role.
        app.MapGet("/api/v1/permissions", async (IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.ListPermissionsAsync(ct)))
            .RequirePermission(Permissions.Role.View)
            .WithTags("Roles");

        return app;
    }
}
