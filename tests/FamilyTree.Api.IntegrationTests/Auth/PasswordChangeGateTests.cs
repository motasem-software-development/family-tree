using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Contracts.Auth;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Auth;

[Collection("postgres")]
public sealed class PasswordChangeGateTests(PostgresFixture fixture) : IAsyncLifetime
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

    private async Task FlagAdminAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
        user.MustChangePassword = true;
        await context.SaveChangesAsync();
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
    public async Task A_flagged_user_cannot_reach_the_member_list()
    {
        await FlagAdminAsync();
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(response)).Should().Be("PASSWORD_CHANGE_REQUIRED");
    }

    [Fact]
    public async Task A_flagged_user_can_still_reach_me()
    {
        await FlagAdminAsync();
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/me");

        // The gate must leave a door open, otherwise the client has no way to discover why
        // it is being refused.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unflagged_user_is_unaffected()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/v1/family-members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_gate_does_not_apply_to_anonymous_requests()
    {
        await FlagAdminAsync();

        // Login must stay reachable — a flagged user has to authenticate before they can
        // change their password.
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
