using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.Roles;
using FamilyTree.Contracts.Users;
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

    [Fact]
    public async Task Creating_a_custom_role_stores_its_permissions()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", "يديرون الأفراد", ["Member.View", "Member.Edit"]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<RoleResponse>())!;
        created.IsSystem.Should().BeFalse();
        created.UserCount.Should().Be(0);
        created.Permissions.Should().BeEquivalentTo("Member.View", "Member.Edit");
    }

    [Fact]
    public async Task An_unknown_permission_code_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", null, ["Member.Teleport"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PERMISSION_NOT_FOUND");
    }

    [Fact]
    public async Task A_duplicate_role_name_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("Viewer", null, ["Member.View"]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("ROLE_NAME_TAKEN");
    }

    [Fact]
    public async Task A_system_role_cannot_be_updated()
    {
        await AuthenticateAsync();
        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");
        var viewer = roles!.Single(r => r.Name == "Viewer");

        var response = await _client.PutAsJsonAsync($"/api/v1/roles/{viewer.Id}",
            new SaveRoleRequest("Viewer", null, ["Member.View", "Member.Delete"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("ROLE_IS_SYSTEM");
    }

    [Fact]
    public async Task A_system_role_cannot_be_deleted()
    {
        await AuthenticateAsync();
        var roles = await _client.GetFromJsonAsync<List<RoleResponse>>("/api/v1/roles");
        var viewer = roles!.Single(r => r.Name == "Viewer");

        var response = await _client.DeleteAsync($"/api/v1/roles/{viewer.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("ROLE_IS_SYSTEM");
    }

    [Fact]
    public async Task A_custom_role_can_be_updated_and_deleted()
    {
        await AuthenticateAsync();

        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", null, ["Member.View"]));
        var role = (await create.Content.ReadFromJsonAsync<RoleResponse>())!;

        var update = await _client.PutAsJsonAsync($"/api/v1/roles/{role.Id}",
            new SaveRoleRequest("أمناء الشجرة", "وصف محدث", ["Member.View", "Member.Create"]));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<RoleResponse>())!;
        updated.Name.Should().Be("أمناء الشجرة");
        updated.Description.Should().Be("وصف محدث");
        updated.Permissions.Should().BeEquivalentTo("Member.View", "Member.Create");

        var delete = await _client.DeleteAsync($"/api/v1/roles/{role.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _client.GetAsync($"/api/v1/roles/{role.Id}");
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_role_that_still_has_members_cannot_be_deleted()
    {
        await AuthenticateAsync();

        var create = await _client.PostAsJsonAsync("/api/v1/roles",
            new SaveRoleRequest("أمناء العائلة", null, ["Member.View"]));
        var role = (await create.Content.ReadFromJsonAsync<RoleResponse>())!;

        var user = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [role.Id]));
        user.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.DeleteAsync($"/api/v1/roles/{role.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("ROLE_IN_USE");
    }
}
