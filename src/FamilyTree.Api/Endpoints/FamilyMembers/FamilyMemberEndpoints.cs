using FamilyTree.Api.Authorization;
using FamilyTree.Api.Endpoints.FamilyTrees;
using FamilyTree.Application.Export;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.Authorization;
using FamilyTree.Domain.Common;

namespace FamilyTree.Api.Endpoints.FamilyMembers;

public static class FamilyMemberEndpoints
{
    public static IEndpointRouteBuilder MapFamilyMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/family-members").WithTags("FamilyMembers");

        // Mirrors FamilyMemberSearchQuery.DefaultLimit, which is internal to Infrastructure and
        // must not be referenced from Api. The service clamps to 1..50 regardless of what
        // arrives here.
        const int defaultSearchLimit = 20;

        // [AsParameters] binds the whole filter set off the query string. Sharing the record
        // with the tree view and the export is what keeps specification §15's combinability
        // structural: a filter added to it reaches all three at once (design spec §5.1).
        group.MapGet("/", async Task<IResult> (
            [AsParameters] MemberFilterRequest request,
            IFamilyMemberService members,
            CancellationToken ct) =>
        {
            if (!MemberFilterBinding.TryBind(request, out var filter, out var error)) return error;

            return Results.Ok(await members.ListAsync(filter, ct));
        })
            .RequirePermission(Permissions.Member.View);

        // Guarded by Member.View, the Members page's own permission — no new permission is
        // introduced for the export (design spec §1.4). Anyone who can see the list can export it.
        //
        // It takes the same [AsParameters] MemberFilterRequest the list takes and re-runs the
        // same query, which is what makes specification §18's "export respects filters" and
        // §27's "export respects permissions" one guarantee rather than two.
        group.MapGet("/export.xlsx", async Task<IResult> (
            [AsParameters] MemberFilterRequest request,
            HttpRequest httpRequest,
            IMemberExcelExporter exporter,
            CancellationToken ct) =>
        {
            if (!MemberFilterBinding.TryBind(request, out var filter, out var error)) return error;

            // The same resolver the PDF export uses, so one Accept-Language header controls the
            // language of both exports.
            var language = CaptionLanguageResolver.Resolve(httpRequest);

            var result = await exporter.ExportAsync(filter, language, ct);

            // Results.File percent-encodes a non-ASCII download name into filename* per RFC 5987,
            // which is what lets an Arabic family name survive the header — the same mechanism
            // export.pdf depends on.
            return Results.File(
                result.Content,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: $"{result.FamilyTreeName}.xlsx");
        })
            .RequirePermission(Permissions.Member.View);

        // Declared before "/{id:guid}" for readability only — the guid route constraint makes
        // the two unambiguous regardless of order.
        group.MapGet("/search", async (
            string? q,
            int? limit,
            int? offset,
            IFamilyMemberService members,
            CancellationToken ct) =>
        {
            // Paging bounds are clamped in the service rather than rejected: a search box
            // sending a stray limit should return sensible results, not a 400 the user cannot
            // act on. The clamp is documented in the README so it is contract, not accident.
            var page = await members.SearchAsync(q ?? string.Empty, limit ?? defaultSearchLimit, offset ?? 0, ct);
            return Results.Ok(page);
        })
            .RequirePermission(Permissions.Member.View);

        group.MapGet("/{id:guid}", async (Guid id, IFamilyMemberService members, CancellationToken ct) =>
        {
            var member = await members.GetAsync(id, ct);
            // Null covers both "no such member" and "belongs to another tenant" — the query
            // filter has already made them the same thing (design spec §4.4). Throwing here
            // (rather than calling ProblemResults.Coded / Results.Problem directly) routes the
            // response through DomainExceptionHandler, the same path PUT and DELETE use, so
            // the body is byte-identical for any unknown id — Results.Problem's own
            // IProblemDetailsService pipeline stamps a per-request traceId that would break
            // that guarantee (design spec §4.4).
            return member is null
                ? throw new NotFoundException("MEMBER_NOT_FOUND", "Member not found.")
                : Results.Ok(member);
        })
            .RequirePermission(Permissions.Member.View);

        group.MapPost("/", async (
            CreateFamilyMemberRequest request, IFamilyMemberService members, CancellationToken ct) =>
        {
            var created = await members.CreateAsync(request, ct);
            return Results.Created($"/api/v1/family-members/{created.Id}", created);
        })
            .RequirePermission(Permissions.Member.Create);

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateFamilyMemberRequest request, IFamilyMemberService members, CancellationToken ct) =>
            Results.Ok(await members.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.Member.Edit);

        // A dedicated command rather than a field on PUT (design spec §4.6): it carries a rule
        // no other edit does, and PUT goes on rejecting parentId outright.
        group.MapPost("/{id:guid}/move", async (
            Guid id, MoveFamilyMemberRequest request, IFamilyMemberService members, CancellationToken ct) =>
            Results.Ok(await members.MoveAsync(id, request, ct)))
            .RequirePermission(Permissions.Member.Move);

        group.MapDelete("/{id:guid}", async (
            Guid id, IFamilyMemberService members, CancellationToken ct) =>
        {
            await members.DeleteAsync(id, ct);
            return Results.NoContent();
        })
            .RequirePermission(Permissions.Member.Delete);

        return app;
    }
}
