using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

/// <summary>
/// The filter set as it arrives over the wire (design spec §5.1) — one shared shape bound by the
/// members list and the tree view, and one code for the one way it can be malformed.
/// </summary>
[Collection("postgres")]
public sealed class MemberFilterEndpointTests(PostgresFixture fixture) : IAsyncLifetime
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

    private async Task<FamilyMemberResponse> CreateAsync(
        string name, Guid? parentId = null, bool isDeceased = false)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members",
            new CreateFamilyMemberRequest(name, parentId) { IsDeceased = isDeceased });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    private Task<List<FamilyMemberListItem>?> ListAsync(string query = "") =>
        _client.GetFromJsonAsync<List<FamilyMemberListItem>>($"/api/v1/family-members{query}");

    [Fact]
    public async Task The_unfiltered_list_is_unchanged()
    {
        // The imported family is seeded, so this is the real 349-member payload plus whatever a
        // sibling assertion added. The contract must not have narrowed.
        await AuthenticateAsync();

        var all = (await ListAsync())!;

        all.Should().HaveCountGreaterThan(300);
        all.Should().Contain(m => m.Name == "داوود");
    }

    [Fact]
    public async Task The_list_carries_the_branch_and_the_generation()
    {
        await AuthenticateAsync();

        var all = (await ListAsync())!;

        var root = all.Single(m => m.ParentId is null);
        root.Should().Match<FamilyMemberListItem>(
            m => m.BranchId == null && m.BranchName == null && m.Generation == 0);
        all.Where(m => m.ParentId == root.Id).Should()
            .OnlyContain(m => m.Generation == 1 && m.BranchId == m.Id);
    }

    [Fact]
    public async Task A_status_filter_narrows_the_list()
    {
        await AuthenticateAsync();
        var root = (await ListAsync())!.Single(m => m.ParentId is null);
        await CreateAsync("زياد", root.Id, isDeceased: true);

        var deceased = (await ListAsync("?status=deceased"))!;

        deceased.Should().OnlyContain(m => m.IsDeceased);
        deceased.Should().Contain(m => m.Name == "زياد");
    }

    [Fact]
    public async Task An_empty_status_is_an_absent_filter_not_an_error()
    {
        // `?status=` and no status at all arrive identically over the wire. Rejecting one and
        // not the other would make a cleared dropdown a 400.
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-members?status=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<FamilyMemberListItem>>())!
            .Should().HaveCountGreaterThan(300);
    }

    [Fact]
    public async Task An_unrecognised_status_is_a_400_with_a_code()
    {
        // Following EXPORT_INVALID_STYLE's precedent: silently defaulting would return a result
        // the caller did not ask for with nothing to say so (design spec §5.1).
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-members?status=dead");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("FILTER_INVALID_STATUS");
    }

    [Fact]
    public async Task The_tree_view_rejects_the_same_status_with_the_same_code()
    {
        // One code, both endpoints — a client must not be able to learn two spellings of the
        // same mistake.
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-tree/view?status=dead");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("FILTER_INVALID_STATUS");
    }

    [Fact]
    public async Task The_filters_combine()
    {
        await AuthenticateAsync();
        var root = (await ListAsync())!.Single(m => m.ParentId is null);
        var branch = await CreateAsync("سليمان المرشَّح", root.Id);
        await CreateAsync("زياد المرشَّح", branch.Id, isDeceased: true);
        await CreateAsync("زياد الحي", branch.Id);

        var narrowed = await ListAsync(
            $"?status=deceased&branchId={branch.Id}&generation=2&search=زياد المرشَّح");

        narrowed!.Select(m => m.Name).Should().Equal("زياد المرشَّح");
    }

    [Fact]
    public async Task An_unmatched_branch_or_country_is_an_empty_list_not_an_error()
    {
        // They are filters. A filter matching nothing is a legitimate answer.
        await AuthenticateAsync();

        (await ListAsync($"?branchId={Guid.CreateVersion7()}"))!.Should().BeEmpty();
        (await ListAsync("?countryId=-1"))!.Should().BeEmpty();
        (await ListAsync("?generation=999"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task The_tree_view_keeps_the_ancestors_of_a_match_and_marks_them()
    {
        // A name the imported family does not already contain: searching for a common one would
        // keep several branches and say nothing about the ancestor rule.
        await AuthenticateAsync();
        var root = (await ListAsync())!.Single(m => m.ParentId is null);
        var branch = await CreateAsync("سليمان المرشَّح", root.Id);
        await CreateAsync("زياد المرشَّح", branch.Id);

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>(
            "/api/v1/family-tree/view?search=زياد المرشَّح");

        var rootNode = view!.RootMembers.Should().ContainSingle().Subject;
        rootNode.Matches.Should().BeFalse();

        var branchNode = rootNode.Children.Should().ContainSingle().Subject;
        branchNode.Name.Should().Be("سليمان المرشَّح");
        branchNode.Matches.Should().BeFalse();

        var match = branchNode.Children.Should().ContainSingle().Subject;
        match.Name.Should().Be("زياد المرشَّح");
        match.Matches.Should().BeTrue();
    }

    [Fact]
    public async Task The_unfiltered_tree_view_marks_every_node_as_a_match()
    {
        await AuthenticateAsync();

        var view = await _client.GetFromJsonAsync<FamilyTreeViewResponse>("/api/v1/family-tree/view");

        Flatten(view!.RootMembers).Should().OnlyContain(node => node.Matches);
    }

    private static IEnumerable<FamilyTreeNodeResponse> Flatten(
        IEnumerable<FamilyTreeNodeResponse> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}
