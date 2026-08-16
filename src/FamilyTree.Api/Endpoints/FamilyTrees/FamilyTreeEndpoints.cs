using FamilyTree.Api.Authorization;
using FamilyTree.Application.FamilyTrees;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.FamilyTrees;

public static class FamilyTreeEndpoints
{
    public static IEndpointRouteBuilder MapFamilyTreeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/family-tree").WithTags("FamilyTree");

        group.MapGet("/", async (IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.GetAsync(ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        group.MapPut("/", async (
            RenameFamilyTreeRequest request, IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.RenameAsync(request, ct)))
            .RequirePermission(Permissions.FamilyTree.Edit);

        group.MapGet("/view", async (
            Guid? rootId, int? maxDepth, IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.GetViewAsync(rootId, maxDepth, ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        return app;
    }
}
