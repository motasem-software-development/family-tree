using FamilyTree.Api.Authorization;
using FamilyTree.Application.Users;
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

        return app;
    }
}
