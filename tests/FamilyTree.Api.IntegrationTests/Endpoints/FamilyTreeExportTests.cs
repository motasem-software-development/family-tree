using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Domain.FamilyTrees;
using FamilyTree.Domain.Tenants;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class FamilyTreeExportTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string ExportPath = "/api/v1/family-tree/export.pdf";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    [Fact]
    public async Task Exporting_without_authentication_is_rejected()
    {
        (await _client.GetAsync(ExportPath)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Exporting_the_seeded_tree_returns_a_pdf()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync(ExportPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task The_download_is_an_attachment_with_a_filename()
    {
        await AuthenticateAsync();

        var disposition = (await _client.GetAsync(ExportPath)).Content.Headers.ContentDisposition!;

        disposition.DispositionType.Should().Be("attachment");
        // Arabic must travel percent-encoded in filename*, never raw in filename.
        (disposition.FileNameStar ?? disposition.FileName).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unknown_root_id_is_not_found()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync($"{ExportPath}?rootId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("MEMBER_NOT_FOUND");
    }

    /// <summary>
    /// Creates a second tenant, with its own tree and one member, entirely separate from the
    /// seeded tenant the HTTP client authenticates as. Mirrors
    /// TenantIsolationTests.SeedTwoTenantsAsync and FamilyMemberSearchTests.SeedTenantWithTreeAsync:
    /// an unfiltered scope (tenant Guid.Empty, matching HttpTenantContext with no HttpContext)
    /// creates the tenant and tree, then a tenant-scoped IFamilyMemberService creates the member
    /// as that tenant would over its own authenticated request.
    /// </summary>
    private async Task<Guid> SeedAnotherTenantsMemberAsync()
    {
        Guid otherTenantId;
        await using (var unfiltered = _factory.Services.CreateAsyncScope())
        {
            var context = unfiltered.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = Tenant.Create("Al-Hassan Family", "export-iso-al-hassan", DateTimeOffset.UtcNow);
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            context.FamilyTrees.Add(
                FamilyTreeAggregate.Create(tenant.Id, "عائلة الحسن", DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();

            otherTenantId = tenant.Id;
        }

        await using var scope = _factory.CreateTenantScope(otherTenantId);
        var members = scope.ServiceProvider.GetRequiredService<IFamilyMemberService>();
        var member = await members.CreateAsync(new CreateFamilyMemberRequest("غريب", null));

        return member.Id;
    }

    [Fact]
    public async Task Another_tenants_member_id_is_not_found_and_indistinguishable_from_unknown()
    {
        await AuthenticateAsync();

        var foreignMemberId = await SeedAnotherTenantsMemberAsync();

        var response = await _client.GetAsync($"{ExportPath}?rootId={foreignMemberId}");

        // Same 404 and same code as a genuinely unknown id (An_unknown_root_id_is_not_found):
        // the caller cannot tell "not yours" from "does not exist anywhere".
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task A_subtree_export_is_smaller_than_the_whole_tree()
    {
        await AuthenticateAsync();

        var whole = await (await _client.GetAsync(ExportPath)).Content.ReadAsByteArrayAsync();

        var members = await _client.GetFromJsonAsync<FamilyMemberResponse[]>("/api/v1/family-members");
        var child = members!.First(m => m.ParentId is not null);

        var subtree = await (await _client.GetAsync($"{ExportPath}?rootId={child.Id}"))
            .Content.ReadAsByteArrayAsync();

        subtree.Length.Should().BeLessThan(whole.Length);
    }
}
