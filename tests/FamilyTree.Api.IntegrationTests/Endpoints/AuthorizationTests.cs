using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.FamilyMembers;
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

    // These three exercise the permission-policy machinery through GET /api/v1/family-tree,
    // which requires FamilyTree.View. They used GET /api/v1/me until /me was deliberately
    // reduced to authentication only (see MeEndpoints) — /me can no longer answer 403, so it
    // cannot serve as the vehicle for a policy test.

    [Fact]
    public async Task A_token_without_the_required_permission_returns_403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.Audit.View));

        var response = await _client.GetAsync("/api/v1/family-tree");

        // Authenticated but not permitted — 403, distinct from the 401 of no token at all.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_with_no_permissions_at_all_returns_403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith());

        var response = await _client.GetAsync("/api/v1/family-tree");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_carrying_the_required_permission_is_admitted_by_the_policy()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.FamilyTree.View));

        var response = await _client.GetAsync("/api/v1/family-tree");

        // The policy admits the request. The tenant in this hand-minted token owns no tree,
        // so the endpoint answers 404 — never 403, and never another tenant's data.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_returns_403_for_a_caller_lacking_family_tree_view()
    {
        // Authenticated, but the export endpoint is guarded by FamilyTree.View like /view is.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.Member.View));

        var response = await _client.GetAsync("/api/v1/family-tree/export.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Me_is_authentication_only_and_never_answers_403()
    {
        // The counterpart of the three above: a token with no permission at all still reads
        // its own identity. The hand-minted tenant owns no tree, so 404 — but never 403.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith());

        var response = await _client.GetAsync("/api/v1/me");

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

    [Fact]
    public async Task Move_member_returns_403_for_a_caller_lacking_the_move_permission()
    {
        // Member.Edit is deliberately present: move is its own permission, and holding the
        // edit permission must not confer the right to restructure the tree.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenWith(Permissions.Member.View, Permissions.Member.Edit));

        var response = await _client.PostAsJsonAsync(
            "/api/v1/family-members/0199a0b1-0000-7000-8000-000000000001/move",
            new MoveFamilyMemberRequest(null, 1));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
