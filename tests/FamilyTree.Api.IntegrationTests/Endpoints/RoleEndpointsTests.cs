using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.Roles;
using FluentAssertions;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class RoleEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
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

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Fact]
    public async Task Listing_roles_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Listing_returns_the_four_seeded_system_roles()
    {
        await AuthenticateAsync();

        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");

        roles.Should().HaveCount(4);
        roles.Should().OnlyContain(r => r.IsSystem);
        roles!.Select(r => r.Name).Should()
            .BeEquivalentTo("Super Admin", "Administrator", "Editor", "Viewer");
    }

    [Fact]
    public async Task Super_admin_carries_every_permission_and_one_user()
    {
        await AuthenticateAsync();

        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");
        var superAdmin = roles!.Single(r => r.Name == "Super Admin");

        superAdmin.Permissions.Should().HaveCount(18);
        superAdmin.UserCount.Should().Be(1);
    }

    [Fact]
    public async Task The_permission_catalog_lists_every_code()
    {
        await AuthenticateAsync();

        var permissions =
            await _client.GetFromJsonAsync<List<PermissionResponse>>("/api/v1/permissions");

        permissions.Should().HaveCount(18);
        permissions!.Select(p => p.Code).Should().Contain("Member.Move");
        // DatabaseSeeder seeds every permission with a null description today; the frontend
        // localizes permission labels from i18n keyed by code instead (bilingual UI, no single
        // server-side string could serve it). This pins current reality so that whoever adds
        // real description text has to consciously touch this test, not silently reshape the
        // contract's nullability.
        permissions.Should().OnlyContain(p => p.Description == null);
    }

    [Fact]
    public async Task Fetching_an_unknown_role_returns_404()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/roles/0199a0b1-0000-7000-8000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
