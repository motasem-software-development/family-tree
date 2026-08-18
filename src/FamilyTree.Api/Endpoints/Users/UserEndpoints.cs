using FamilyTree.Api.Authorization;
using FamilyTree.Application.Users;
using FamilyTree.Contracts.Users;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");

        group.MapGet("/", async (IUserService users, CancellationToken ct) =>
            Results.Ok(await users.ListAsync(ct)))
            .RequirePermission(Permissions.User.View);

        group.MapGet("/{id:guid}", async (Guid id, IUserService users, CancellationToken ct) =>
            await users.GetAsync(id, ct) is { } user ? Results.Ok(user) : Results.NotFound())
            .RequirePermission(Permissions.User.View);

        group.MapPost("/", async (
            CreateUserRequest request, IUserService users, CancellationToken ct) =>
        {
            var created = await users.CreateAsync(request, ct);
            return Results.Created($"/api/v1/users/{created.Id}", created);
        })
            .RequirePermission(Permissions.User.Create);

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateUserRequest request, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.User.Edit);

        group.MapPost("/{id:guid}/activate", async (
            Guid id, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.SetActiveAsync(id, isActive: true, ct)))
            .RequirePermission(Permissions.User.Deactivate);

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.SetActiveAsync(id, isActive: false, ct)))
            .RequirePermission(Permissions.User.Deactivate);

        group.MapPost("/{id:guid}/password", async (
            Guid id, ResetPasswordRequest request, IUserService users, CancellationToken ct) =>
            Results.Ok(await users.ResetPasswordAsync(id, request, ct)))
            .RequirePermission(Permissions.User.Edit);

        return app;
    }
}
