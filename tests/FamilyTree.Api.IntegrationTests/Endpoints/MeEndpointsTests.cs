using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;

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
