using FamilyTree.Api.Authorization;
using FamilyTree.Api.Endpoints.FamilyMembers;
using FamilyTree.Api.Errors;
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyMembers;
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

        // The same filter set as the members list, and the same 400 for the same bad status —
        // one code, both endpoints. maxDepth stays outside it: it is a transport concern (how
        // much of the tree to ship), not a filter (design spec §5.1).
        group.MapGet("/view", async Task<IResult> (
            [AsParameters] MemberFilterRequest request,
            int? maxDepth,
            IFamilyTreeService trees,
            CancellationToken ct) =>
        {
            if (!MemberFilterBinding.TryBind(request, out var filter, out var error)) return error;

            return Results.Ok(await trees.GetViewAsync(filter, maxDepth, ct));
        })
            .RequirePermission(Permissions.FamilyTree.View);

        // Reference data for the filter controls (design spec §5.1). Both take only rootId:
        // they answer "what is available to filter by", so narrowing them by the rest of the
        // filter would build a dropdown that erases its own options as soon as one is used.
        //
        // A rootId naming nothing returns an empty list rather than a 404 — "this subtree has no
        // branches" and "no such subtree" are the same answer to a dropdown, and design spec
        // §4.4's uniform 404 is about reads of members, not reference lists.
        group.MapGet("/branches", async (
            Guid? rootId, IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.ListBranchesAsync(rootId, ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        group.MapGet("/generations", async (
            Guid? rootId, IFamilyTreeService trees, CancellationToken ct) =>
            Results.Ok(await trees.ListGenerationsAsync(rootId, ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        // Guarded by FamilyTree.View, not a new permission: the export reveals exactly the data
        // /view already returns, so a separate code would add a lockout surface the
        // last-administrator guard has to reason about, without adding protection (design §5.1).
        group.MapGet("/export.pdf", async Task<IResult> (
            Guid? rootId,
            int? maxDepth,
            string? style,
            string? page,
            HttpRequest request,
            IFamilyTreeExporter exporter,
            CancellationToken ct) =>
        {
            // Spec §5.1 specifies defaults for ABSENT parameters, not for invalid ones. Silently
            // treating style=clea as the default produced a 200 carrying a different document
            // than the caller asked for, with nothing to tell them so. Both selectors are
            // case-insensitive, and both still default when omitted.
            if (!TryParseStyle(style, out var chosenStyle))
                return ProblemResults.Coded(
                    StatusCodes.Status400BadRequest,
                    "EXPORT_INVALID_STYLE",
                    "Unknown export style. Use 'xmind' or 'clean'.");

            if (!TryParsePageFormat(page, out var format))
                return ProblemResults.Coded(
                    StatusCodes.Status400BadRequest,
                    "EXPORT_INVALID_PAGE",
                    "Unknown export page format. Use 'sheet' or 'a4'.");

            var language = CaptionLanguageResolver.Resolve(request);

            var result = await exporter.ExportAsync(rootId, maxDepth, chosenStyle, format, language, ct);

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

    /// <summary>Absent means the default (design §5.1); anything unrecognised is a 400.</summary>
    private static bool TryParseStyle(string? style, out ExportStyle parsed)
    {
        parsed = ExportStyle.Xmind;
        if (style is null) return true;

        if (string.Equals(style, "xmind", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(style, "clean", StringComparison.OrdinalIgnoreCase))
        {
            parsed = ExportStyle.Clean;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The format still crosses the Application boundary as a string, not as
    /// <c>ExportPageFormat</c> -- that conversion is deliberately parked. Validating it here
    /// means the only strings that ever reach the adapter are the two it understands.
    /// </summary>
    private static bool TryParsePageFormat(string? page, out string parsed)
    {
        parsed = "sheet";
        if (page is null) return true;

        if (string.Equals(page, "sheet", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(page, "a4", StringComparison.OrdinalIgnoreCase))
        {
            parsed = "a4";
            return true;
        }

        return false;
    }
}
