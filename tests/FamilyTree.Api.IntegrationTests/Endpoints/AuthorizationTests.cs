using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Domain.Authorization;
using FamilyTree.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Endpoints;

[Collection("postgres")]
public sealed class AuthorizationTests(PostgresFixture fixture) : IAsyncLifetime
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

    /// <summary>Mints a token directly so a permission set can be varied without seeding users.</summary>
    private string TokenWith(params string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        return tokens.CreateAccessToken(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "someone@example.com", permissions,
            mustChangePassword: false).Value;
    }

    [Fact]
    public async Task A_token_without_the_required_permission_returns_403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.Audit.View));

        var response = await _client.GetAsync("/api/v1/me");

        // Authenticated but not permitted — 403, distinct from the 401 of no token at all.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_with_no_permissions_at_all_returns_403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith());

        var response = await _client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_carrying_the_required_permission_is_admitted_by_the_policy()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.FamilyTree.View));

        var response = await _client.GetAsync("/api/v1/me");

        // The policy admits the request. The tenant in this hand-minted token owns no tree,
        // so the endpoint answers 404 — never 403, and never another tenant's data.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_member_returns_403_for_a_caller_lacking_the_delete_permission()
    {
        // Authenticated, and permitted to view members, but not to delete them.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.Member.View));

        var response = await _client.DeleteAsync($"/api/v1/family-members/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
