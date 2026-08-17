using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class FamilyMemberEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
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

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Theory]
    [InlineData("GET", "/api/v1/family-members")]
    [InlineData("POST", "/api/v1/family-members")]
    [InlineData("GET", "/api/v1/family-members/0199a0b1-0000-7000-8000-000000000001")]
    [InlineData("PUT", "/api/v1/family-members/0199a0b1-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/v1/family-members/0199a0b1-0000-7000-8000-000000000001")]
    public async Task Endpoints_require_authentication(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new CreateFamilyMemberRequest("فارس", null));
        if (method == "PUT")
            request.Content = JsonContent.Create(new UpdateFamilyMemberRequest("فارس", 1));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_creates_a_first_generation_member_and_returns_its_location()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("سليمان", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        created.Name.Should().Be("سليمان");
        created.ParentId.Should().BeNull();
        response.Headers.Location!.ToString().Should().EndWith($"/api/v1/family-members/{created.Id}");
    }

    [Fact]
    public async Task Post_creates_a_descendant_under_an_existing_parent()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");

        var child = await CreateAsync("فارس", parent.Id);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task Post_rejects_a_blank_name_with_a_stable_code()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("   ", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_NAME_REQUIRED");
    }

    [Fact]
    public async Task Post_rejects_an_unknown_parent()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members", new CreateFamilyMemberRequest("فارس", Guid.CreateVersion7()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_PARENT_NOT_FOUND");
    }

    [Fact]
    public async Task Get_returns_the_member()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("سليمان");

        var response = await _client.GetAsync($"/api/v1/family-members/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!.Name.Should().Be("سليمان");
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_id()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(response)).Should().Be("MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task Get_list_returns_every_member_of_the_tenant()
    {
        await AuthenticateAsync();
        var before = (await _client.GetFromJsonAsync<List<FamilyMemberResponse>>("/api/v1/family-members"))!.Count;
        await CreateAsync("سليمان");
        await CreateAsync("عمر");

        var members = await _client.GetFromJsonAsync<List<FamilyMemberResponse>>("/api/v1/family-members");

        members.Should().HaveCount(before + 2);
    }

    [Fact]
    public async Task Put_renames_a_member()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("فارس");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("فارس أحمد", created.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        updated.Name.Should().Be("فارس أحمد");
        updated.Version.Should().Be(created.Version + 1);
    }

    [Fact]
    public async Task Put_returns_409_for_a_stale_version()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("أحمد");
        await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("أحمد علي", created.Version));

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("أحمد محمد", created.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task Put_rejects_an_attempt_to_change_the_parent()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");
        var child = await CreateAsync("فارس");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{child.Id}",
            new UpdateFamilyMemberRequest("فارس", child.Version, ParentId: parent.Id));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_FIELD_NOT_UPDATABLE");
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_id()
    {
        await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{Guid.CreateVersion7()}",
            new UpdateFamilyMemberRequest("فارس", 1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_removes_a_leaf_member()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("فارس");

        var response = await _client.DeleteAsync($"/api/v1/family-members/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/v1/family-members/{created.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_returns_409_when_the_member_has_children()
    {
        await AuthenticateAsync();
        var parent = await CreateAsync("سليمان");
        await CreateAsync("فارس", parent.Id);

        var response = await _client.DeleteAsync($"/api/v1/family-members/{parent.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("MEMBER_HAS_CHILDREN");
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_id()
    {
        await AuthenticateAsync();

        var response = await _client.DeleteAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_404_body_carries_no_information_about_which_id_was_missing()
    {
        // Design spec §4.4 — two different unknown ids must produce byte-identical bodies,
        // so a caller cannot probe for the existence of another tenant's member.
        await AuthenticateAsync();

        var first = await _client.GetAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");
        var second = await _client.GetAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        first.StatusCode.Should().Be(second.StatusCode);
        (await first.Content.ReadAsStringAsync()).Should().Be(await second.Content.ReadAsStringAsync());
    }
}
