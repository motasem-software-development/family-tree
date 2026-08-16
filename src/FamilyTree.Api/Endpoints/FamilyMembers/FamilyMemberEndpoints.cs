using FamilyTree.Api.Authorization;
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

        group.MapGet("/", async (IFamilyMemberService members, CancellationToken ct) =>
            Results.Ok(await members.ListAsync(ct)))
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
