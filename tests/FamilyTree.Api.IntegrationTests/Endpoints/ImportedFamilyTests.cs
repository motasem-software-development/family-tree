using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

/// <summary>
/// Verifies the seed migration reproduces the human-reviewed artifact
/// (<c>docs/import/family-tree.json</c>) exactly: 349 members, a single root (داوود), and every
/// member reachable from that root.
/// </summary>
[Collection("postgres")]
public sealed class ImportedFamilyTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int ExpectedMemberCount = 349;
    private const string RootName = "داوود";

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        await _factory.ResetAndSeedAsync();
        _client = _factory.CreateClient();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Seeded_tree_matches_the_reviewed_artifact()
    {
        var members = await _client.GetFromJsonAsync<FamilyMemberResponse[]>("/api/v1/family-members");

        members!.Length.Should().Be(ExpectedMemberCount);
        members.Where(m => m.ParentId is null).Should().ContainSingle()
            .Which.Name.Should().Be(RootName);
    }

    [Fact]
    public async Task Whole_tree_is_reachable_from_the_root()
    {
        // The composite (family_tree_id, tenant_id) FK should make a stray row impossible, so
        // this guards the migration's literal values rather than the constraint itself.
        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>("/api/v1/family-tree/view");

        var root = view!.RootMembers.Should().ContainSingle().Subject;
        root.Name.Should().Be(RootName);
        CountNodes(view.RootMembers).Should().Be(ExpectedMemberCount);
    }

    private static int CountNodes(IReadOnlyList<FamilyTreeNodeResponse> nodes) =>
        nodes.Count + nodes.Sum(n => CountNodes(n.Children));
}
