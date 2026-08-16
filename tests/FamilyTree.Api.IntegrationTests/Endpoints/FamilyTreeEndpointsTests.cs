using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class FamilyTreeEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
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

    private async Task<FamilyMemberResponse> CreateAsync(string name, Guid? parentId = null)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest(name, parentId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
    }

    [Fact]
    public async Task Get_requires_authentication()
    {
        (await _client.GetAsync("/api/v1/family-tree")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task View_requires_authentication()
    {
        (await _client.GetAsync("/api/v1/family-tree/view")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Put_requires_authentication()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/family-tree", new RenameFamilyTreeRequest("عائلة السقا الكرام"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_returns_the_seeded_root_family()
    {
        await AuthenticateAsync();

        var tree = await _client.GetFromJsonAsync<FamilyTreeResponse>("/api/v1/family-tree");

        tree!.Name.Should().Be("عائلة السقا");
        tree.MemberCount.Should().Be(0);
    }

    [Fact]
    public async Task Get_counts_the_members()
    {
        await AuthenticateAsync();
        await CreateAsync("سليمان");
        await CreateAsync("عمر");

        var tree = await _client.GetFromJsonAsync<FamilyTreeResponse>("/api/v1/family-tree");

        tree!.MemberCount.Should().Be(2);
    }

    [Fact]
    public async Task Put_renames_the_root_family()
    {
        await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync(
            "/api/v1/family-tree", new RenameFamilyTreeRequest("عائلة السقا الكرام"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FamilyTreeResponse>())!
            .Name.Should().Be("عائلة السقا الكرام");
    }

    [Fact]
    public async Task Put_rejects_a_blank_name()
    {
        await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync(
            "/api/v1/family-tree", new RenameFamilyTreeRequest("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task View_returns_the_root_family_with_no_members_when_the_tree_is_empty()
    {
        await AuthenticateAsync();

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>("/api/v1/family-tree/view");

        view!.Name.Should().Be("عائلة السقا");
        view.RootMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task View_returns_the_nested_hierarchy_with_generations()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        await CreateAsync("محمود", faris.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>("/api/v1/family-tree/view");

        var root = view!.RootMembers.Should().ContainSingle().Subject;
        root.Name.Should().Be("سليمان");
        root.Generation.Should().Be(1);
        root.Children[0].Name.Should().Be("فارس");
        root.Children[0].Generation.Should().Be(2);
        root.Children[0].Children[0].Name.Should().Be("محمود");
        root.Children[0].Children[0].Generation.Should().Be(3);
    }

    [Fact]
    public async Task View_honours_max_depth_and_flags_truncated_nodes()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        await CreateAsync("محمود", faris.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            "/api/v1/family-tree/view?maxDepth=2");

        var farisNode = view!.RootMembers[0].Children.Should().ContainSingle().Subject;
        farisNode.Children.Should().BeEmpty();
        farisNode.HasMoreChildren.Should().BeTrue();
    }

    [Fact]
    public async Task View_honours_root_id()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        await CreateAsync("محمود", faris.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            $"/api/v1/family-tree/view?rootId={faris.Id}");

        var root = view!.RootMembers.Should().ContainSingle().Subject;
        root.Name.Should().Be("فارس");
        root.Generation.Should().Be(2);
    }

    [Fact]
    public async Task View_returns_an_empty_member_list_for_an_unknown_root_id()
    {
        await AuthenticateAsync();
        await CreateAsync("سليمان");

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            $"/api/v1/family-tree/view?rootId={Guid.CreateVersion7()}");

        view!.RootMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task View_applies_max_depth_relative_to_the_requested_root()
    {
        await AuthenticateAsync();
        var suleiman = await CreateAsync("سليمان");
        var faris = await CreateAsync("فارس", suleiman.Id);
        var mahmoud = await CreateAsync("محمود", faris.Id);
        await CreateAsync("خالد", mahmoud.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            $"/api/v1/family-tree/view?rootId={faris.Id}&maxDepth=2");

        var root = view!.RootMembers.Should().ContainSingle().Subject;
        root.Name.Should().Be("فارس");
        // Generation is absolute — فارس really is the second generation of the family.
        root.Generation.Should().Be(2);

        // maxDepth counts from the requested root, so two levels means فارس and محمود.
        var mahmoudNode = root.Children.Should().ContainSingle().Subject;
        mahmoudNode.Name.Should().Be("محمود");
        mahmoudNode.Generation.Should().Be(3);
        mahmoudNode.Children.Should().BeEmpty();
        mahmoudNode.HasMoreChildren.Should().BeTrue("خالد exists but was not returned");
    }
}
