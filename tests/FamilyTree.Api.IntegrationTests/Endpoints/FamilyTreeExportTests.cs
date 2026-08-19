using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

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
