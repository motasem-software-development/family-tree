using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Contracts.Users;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class MeEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
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

    [Fact]
    public async Task Me_without_a_token_returns_401()
    {
        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_with_a_malformed_token_returns_401()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_seeded_super_admin_with_the_full_permission_set()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!;

        me.Email.Should().Be(ApiFactory.AdminEmail);
        me.TenantId.Should().NotBeEmpty();
        me.FamilyTreeName.Should().Be("عائلة السقا");
        me.Permissions.Should().Contain("Member.Create").And.Contain("Role.Delete");
    }

    [Fact]
    public async Task Me_answers_a_user_who_holds_no_permissions_at_all()
    {
        // /me must never depend on a permission. A user's role set is optional, so a real
        // account can hold nothing at all — and if /me answered 403 for them, the frontend
        // would have no identity to read, ProtectedRoute would bounce them back to /login,
        // and the sign-in form would render again with no error: an unbreakable loop. Worse
        // for a newly created user, who is always flagged for a password change: the gate
        // permits exactly GET /me and POST /me/password, and the change-password screen needs
        // the email /me carries in order to sign back in.
        await AuthenticateAsync();

        var create = await _client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("roleless@example.com", "Temp0rary!Password", []));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        using var theirs = _factory.CreateClient();
        var login = await theirs.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("roleless@example.com", "Temp0rary!Password"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        theirs.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await theirs.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!;
        me.Email.Should().Be("roleless@example.com");
        me.Permissions.Should().BeEmpty();
        me.MustChangePassword.Should().BeTrue();
        // The tree name is not a leak: this is an authenticated member of that very tenant.
        me.FamilyTreeName.Should().Be("عائلة السقا");
    }

    [Fact]
    public async Task Me_resolves_the_tenant_from_the_token_and_ignores_a_spoofed_header()
    {
        await AuthenticateAsync();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.CreateVersion7().ToString());

        var response = await _client.GetAsync("/api/v1/me");
        var me = (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!;

        // The family tree name proves the tenant came from the token, not the header.
        me.FamilyTreeName.Should().Be("عائلة السقا");
    }
}
