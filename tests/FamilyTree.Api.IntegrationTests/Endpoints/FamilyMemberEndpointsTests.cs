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

    [Fact]
    public async Task Search_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/family-members/search?q=محمد");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_returns_matches_with_their_ancestor_path()
    {
        await AuthenticateAsync();
        var root = await CreateAsync("داوود");
        var middle = await CreateAsync("سلمان", root.Id);
        var leaf = await CreateAsync("خالد-اختبار", middle.Id);

        var page = await _client.GetFromJsonAsync<FamilyMemberSearchResponse>(
            "/api/v1/family-members/search?q=خالد-اختبار");

        page!.Total.Should().Be(1);
        var hit = page.Items.Should().ContainSingle().Subject;
        hit.Id.Should().Be(leaf.Id);
        hit.Ancestors.Select(a => a.Name).Should().Equal("داوود", "سلمان");
        hit.Generation.Should().Be(3);
    }

    [Fact]
    public async Task Search_reports_the_true_total_alongside_a_smaller_page()
    {
        await AuthenticateAsync();
        var root = await CreateAsync("داوود");
        for (var i = 0; i < 4; i++) await CreateAsync("محمد-اختبار", root.Id);

        var page = await _client.GetFromJsonAsync<FamilyMemberSearchResponse>(
            "/api/v1/family-members/search?q=محمد-اختبار&limit=2");

        page!.Total.Should().Be(4);
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_without_a_query_returns_an_empty_page_rather_than_an_error()
    {
        await AuthenticateAsync();
        await CreateAsync("داوود");

        var response = await _client.GetAsync("/api/v1/family-members/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<FamilyMemberSearchResponse>())!;
        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_returns_an_empty_page_for_a_name_that_does_not_exist()
    {
        await AuthenticateAsync();
        await CreateAsync("داوود");

        var response = await _client.GetAsync("/api/v1/family-members/search?q=لايوجد");

        // 200 with nothing in it, not 404: a "not found" here would let a caller probe for
        // which names exist (design spec §4.4).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FamilyMemberSearchResponse>())!.Total.Should().Be(0);
    }

    // ---- Life details ----

    [Fact]
    public async Task Post_round_trips_the_life_details()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/family-members",
            new CreateFamilyMemberRequest(
                "سليمان", null, new DateOnly(1920, 3, 14), new DateOnly(1995, 11, 2), IsDeceased: true));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        created.DateOfBirth.Should().Be(new DateOnly(1920, 3, 14));
        created.DateOfDeath.Should().Be(new DateOnly(1995, 11, 2));
        created.IsDeceased.Should().BeTrue();
    }

    [Fact]
    public async Task Post_defaults_a_member_to_living_with_no_dates()
    {
        await AuthenticateAsync();

        var created = await CreateAsync("فارس");

        created.DateOfBirth.Should().BeNull();
        created.DateOfDeath.Should().BeNull();
        created.IsDeceased.Should().BeFalse();
    }

    [Fact]
    public async Task Post_rejects_a_death_date_before_the_birth_date()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/family-members",
            new CreateFamilyMemberRequest(
                "سليمان", null, new DateOnly(1995, 11, 2), new DateOnly(1920, 3, 14), IsDeceased: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_DEATH_BEFORE_BIRTH");
    }

    [Fact]
    public async Task Post_rejects_a_birth_date_in_the_future()
    {
        await AuthenticateAsync();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var response = await _client.PostAsJsonAsync("/api/v1/family-members",
            new CreateFamilyMemberRequest("سليمان", null, tomorrow));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_DATE_IN_FUTURE");
    }

    [Fact]
    public async Task Put_updates_the_life_details_alongside_the_name_in_one_version_bump()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("فارس");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest(
                "فارس أحمد",
                created.Version,
                DateOfBirth: new DateOnly(1940, 1, 5),
                DateOfDeath: new DateOnly(2010, 6, 30),
                IsDeceased: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        updated.Name.Should().Be("فارس أحمد");
        updated.DateOfBirth.Should().Be(new DateOnly(1940, 1, 5));
        updated.DateOfDeath.Should().Be(new DateOnly(2010, 6, 30));
        updated.IsDeceased.Should().BeTrue();
        // One save is one edit — the client's returned version must not already be stale.
        updated.Version.Should().Be(created.Version + 1);
    }

    [Fact]
    public async Task Put_marks_a_member_deceased_when_only_a_death_date_is_sent()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("سليمان");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest(
                "سليمان", created.Version, DateOfDeath: new DateOnly(1995, 11, 2)));

        var updated = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        updated.IsDeceased.Should().BeTrue();
    }

    [Fact]
    public async Task Put_can_clear_the_life_details_again()
    {
        // A mistaken death record has to be correctable, or a misclick is permanent.
        await AuthenticateAsync();
        var create = await _client.PostAsJsonAsync("/api/v1/family-members",
            new CreateFamilyMemberRequest(
                "سليمان", null, new DateOnly(1920, 3, 14), new DateOnly(1995, 11, 2), IsDeceased: true));
        var created = (await create.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest("سليمان", created.Version));

        var updated = (await response.Content.ReadFromJsonAsync<FamilyMemberResponse>())!;
        updated.DateOfBirth.Should().BeNull();
        updated.DateOfDeath.Should().BeNull();
        updated.IsDeceased.Should().BeFalse();
    }

    [Fact]
    public async Task Put_rejects_a_death_date_before_the_birth_date()
    {
        await AuthenticateAsync();
        var created = await CreateAsync("سليمان");

        var response = await _client.PutAsJsonAsync($"/api/v1/family-members/{created.Id}",
            new UpdateFamilyMemberRequest(
                "سليمان",
                created.Version,
                DateOfBirth: new DateOnly(1995, 11, 2),
                DateOfDeath: new DateOnly(1920, 3, 14),
                IsDeceased: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("MEMBER_DEATH_BEFORE_BIRTH");
    }
}
