using FamilyTree.Api.Authorization;
using FamilyTree.Application.Export;
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

        // Guarded by FamilyTree.View, not a new permission: the export reveals exactly the data
        // /view already returns, so a separate code would add a lockout surface the
        // last-administrator guard has to reason about, without adding protection (design §5.1).
        group.MapGet("/export.pdf", async (
            Guid? rootId,
            int? maxDepth,
            string? style,
            string? page,
            IFamilyTreeExporter exporter,
            CancellationToken ct) =>
        {
            var chosenStyle = string.Equals(style, "clean", StringComparison.OrdinalIgnoreCase)
                ? ExportStyle.Clean
                : ExportStyle.Xmind;

            var format = string.Equals(page, "a4", StringComparison.OrdinalIgnoreCase)
                ? "a4"
                : "sheet";

            var result = await exporter.ExportAsync(rootId, maxDepth, chosenStyle, format, ct);

            // Results.File percent-encodes a non-ASCII download name into filename* per
            // RFC 5987, which is what lets an Arabic family name survive the header.
            return Results.File(
                result.Content,
                contentType: "application/pdf",
                fileDownloadName: $"{result.FamilyTreeName}.pdf");
        })
            .RequirePermission(Permissions.FamilyTree.View);

        return app;
    }
}
