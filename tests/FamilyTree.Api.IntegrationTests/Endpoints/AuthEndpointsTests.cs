using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class AuthEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
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

    private async Task<LoginResponse> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_an_access_token_and_a_refresh_token()
    {
        var login = await LoginAsync();

        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_returns_401_with_a_stable_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, "not-the-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_with_an_unknown_email_returns_the_same_401_and_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("nobody@example.com", "whatever"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Identical to the wrong-password response: the API must not reveal which emails exist.
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_with_a_blank_email_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("", "whatever"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_returns_a_new_token_pair()
    {
        var login = await LoginAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(login.RefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        refreshed.RefreshToken.Should().NotBe(login.RefreshToken, "tokens rotate on use");
        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_refresh_token_cannot_be_used_twice()
    {
        var login = await LoginAsync();

        await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        var replay = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await replay.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_with_a_fabricated_token_returns_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest("this-was-never-issued"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var login = await LoginAsync();

        var logout = await _client.PostAsJsonAsync("/api/v1/auth/logout",
            new RefreshRequest(login.RefreshToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterLogout = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(login.RefreshToken));
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_with_an_unknown_token_still_returns_204()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/logout",
            new RefreshRequest("never-issued"));

        // Logout is idempotent and must not become an oracle for which tokens exist.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
