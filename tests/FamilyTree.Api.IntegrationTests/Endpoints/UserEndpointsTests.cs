using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.Users;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class UserEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
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

    private async Task<Guid> RoleIdAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == name).Select(r => r.Id).SingleAsync();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem is not null && problem.TryGetValue("code", out var code) ? code.ToString() : null;
    }

    [Fact]
    public async Task Listing_users_requires_authentication()
    {
        var response = await _client.GetAsync("/api/v1/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Listing_returns_the_seeded_admin_with_its_role()
    {
        await AuthenticateAsync();

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");

        users.Should().ContainSingle();
        var admin = users![0];
        admin.Email.Should().Be(ApiFactory.AdminEmail);
        admin.IsActive.Should().BeTrue();
        admin.MustChangePassword.Should().BeFalse();
        admin.Roles.Should().ContainSingle().Which.Name.Should().Be("Super Admin");
    }

    [Fact]
    public async Task Fetching_an_unknown_user_returns_404()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/users/0199a0b1-0000-7000-8000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_user_assigns_roles_and_forces_a_password_change()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        created.Email.Should().Be("cousin@example.com");
        created.IsActive.Should().BeTrue();
        // The administrator chose this password, so the new user must replace it (spec §4.9).
        created.MustChangePassword.Should().BeTrue();
        created.Roles.Should().ContainSingle().Which.Name.Should().Be("Viewer");
    }

    [Fact]
    public async Task A_created_user_can_log_in_and_is_immediately_gated()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));

        using var fresh = _factory.CreateClient();
        var login = await fresh.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        fresh.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var blocked = await fresh.GetAsync("/api/v1/family-members");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(blocked)).Should().Be("PASSWORD_CHANGE_REQUIRED");
    }

    [Fact]
    public async Task A_duplicate_email_is_rejected()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest(ApiFactory.AdminEmail, "Temp0rary!Password", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("USER_EMAIL_TAKEN");
    }

    [Fact]
    public async Task An_unknown_role_id_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password",
                [Guid.Parse("0199a0b1-0000-7000-8000-000000000001")]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("ROLE_NOT_FOUND");
    }

    [Fact]
    public async Task A_short_password_is_rejected()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "short", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PASSWORD_TOO_SHORT");
    }

    [Fact]
    public async Task Updating_replaces_the_role_set()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");
        var editorRoleId = await RoleIdAsync("Editor");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new UpdateUserRequest("cousin@example.com", [editorRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        updated.Roles.Should().ContainSingle().Which.Name.Should().Be("Editor");
    }

    [Fact]
    public async Task Updating_an_unknown_user_returns_404()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var response = await _client.PutAsJsonAsync(
            "/api/v1/users/0199a0b1-0000-7000-8000-000000000001",
            new UpdateUserRequest("nobody@example.com", [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stripping_the_last_administrators_roles_is_rejected()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");
        var admin = users!.Single(u => u.Email == ApiFactory.AdminEmail);

        // Viewer holds neither User.Edit nor Role.Edit, so this would leave the tenant unable
        // to manage itself.
        var response = await _client.PutAsJsonAsync($"/api/v1/users/{admin.Id}",
            new UpdateUserRequest(ApiFactory.AdminEmail, [viewerRoleId]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task Demoting_an_administrator_is_allowed_when_another_remains()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");
        var superAdminRoleId = await RoleIdAsync("Super Admin");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("second@example.com", "Temp0rary!Password", [superAdminRoleId]));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");
        var admin = users!.Single(u => u.Email == ApiFactory.AdminEmail);

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{admin.Id}",
            new UpdateUserRequest(ApiFactory.AdminEmail, [viewerRoleId]));

        // The mirror of the previous test: the guard must permit exactly the case that is safe,
        // otherwise it is indistinguishable from "administrators can never be demoted".
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivating_a_user_blocks_their_login_and_kills_their_refresh_token()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        using var theirs = _factory.CreateClient();
        var login = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        var deactivate = await _client.PostAsync($"/api/v1/users/{user.Id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivate.Content.ReadFromJsonAsync<UserResponse>())!.IsActive.Should().BeFalse();

        // AuthEndpoints maps ACCOUNT_INACTIVE to 403, distinct from the 401 used for bad
        // credentials — see AuthEndpoints.StatusForCode.
        var again = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        again.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(again)).Should().Be("ACCOUNT_INACTIVE");

        var refresh = await theirs.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reactivating_restores_login()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        await _client.PostAsync($"/api/v1/users/{user.Id}/deactivate", null);
        var activate = await _client.PostAsync($"/api/v1/users/{user.Id}/activate", null);

        activate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var theirs = _factory.CreateClient();
        var login = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivating_the_last_administrator_is_rejected()
    {
        await AuthenticateAsync();

        var users = await _client.GetFromJsonAsync<List<UserResponse>>("/api/v1/users");
        var admin = users!.Single(u => u.Email == ApiFactory.AdminEmail);

        var response = await _client.PostAsync($"/api/v1/users/{admin.Id}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task An_administrator_reset_forces_the_user_to_change_it_again()
    {
        await AuthenticateAsync();
        var viewerRoleId = await RoleIdAsync("Viewer");

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("cousin@example.com", "Temp0rary!Password", [viewerRoleId]));
        var user = (await create.Content.ReadFromJsonAsync<UserResponse>())!;

        // Clear the flag first, so the assertion below proves the reset SET it rather than
        // merely observing the value creation left behind.
        using var theirs = _factory.CreateClient();
        var firstLogin = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "Temp0rary!Password"));
        var tokens = (await firstLogin.Content.ReadFromJsonAsync<LoginResponse>())!;
        theirs.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var selfChange = await theirs.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest("Temp0rary!Password", "Ch0sen!ByThe#User"));
        selfChange.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reset = await _client.PostAsJsonAsync($"/api/v1/users/{user.Id}/password",
            new ResetPasswordRequest("R3set!ByAdmin#Pass"));

        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reset.Content.ReadFromJsonAsync<UserResponse>())!.MustChangePassword.Should().BeTrue();

        using var after = _factory.CreateClient();
        var login = await after.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("cousin@example.com", "R3set!ByAdmin#Pass"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
