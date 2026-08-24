using FamilyTree.Application.Countries;

namespace FamilyTree.Api.Endpoints.Countries;

public static class CountryEndpoints
{
    public static IEndpointRouteBuilder MapCountryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/countries").WithTags("Countries");

        // Authenticated but deliberately not permission-guarded (design §5.2): this is public
        // reference data, and requiring Member.View would break the member form's country
        // dropdown for a user who can edit members but not browse the list.
        group.MapGet("/", async (ICountryService countries, CancellationToken ct) =>
            Results.Ok(await countries.ListAsync(ct)))
            .RequireAuthorization();

        return app;
    }
}
