using FamilyTree.Api.Authorization;
using FamilyTree.Application.Reports;
using FamilyTree.Domain.Authorization;

namespace FamilyTree.Api.Endpoints.Reports;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports");

        // Guarded by FamilyTree.View, not a new permission: every figure here is an aggregate
        // over data that permission already exposes, so a separate code would add a lockout
        // surface for the last-administrator guard to reason about without adding protection.
        // Same reasoning as GET /api/v1/family-tree/export.pdf (design §4).
        //
        // No query parameters: the windows and caps are fixed constants in ReportLimits, which
        // keeps the response one cacheable shape with no validation surface.
        group.MapGet("/", async (IReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.GetAsync(ct)))
            .RequirePermission(Permissions.FamilyTree.View);

        return app;
    }
}
