using FamilyTree.Api.Authorization;
using FamilyTree.Application.Roles;
using FamilyTree.Contracts.Roles;
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

        group.MapPost("/", async (
            SaveRoleRequest request, IRoleService roles, CancellationToken ct) =>
        {
            var created = await roles.CreateAsync(request, ct);
            return Results.Created($"/api/v1/roles/{created.Id}", created);
        })
            .RequirePermission(Permissions.Role.Create);

        group.MapPut("/{id:guid}", async (
            Guid id, SaveRoleRequest request, IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.Role.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id, IRoleService roles, CancellationToken ct) =>
        {
            await roles.DeleteAsync(id, ct);
            return Results.NoContent();
        })
            .RequirePermission(Permissions.Role.Delete);

        // Outside the /roles group: it is the catalog the role editor reads, not a role.
        app.MapGet("/api/v1/permissions", async (IRoleService roles, CancellationToken ct) =>
            Results.Ok(await roles.ListPermissionsAsync(ct)))
            .RequirePermission(Permissions.Role.View)
            .WithTags("Roles");

        return app;
    }
}
