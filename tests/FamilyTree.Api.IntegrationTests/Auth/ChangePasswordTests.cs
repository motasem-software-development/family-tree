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
public sealed class ChangePasswordTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string NewPassword = "An0ther!Str0ng#Password";

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
    public async Task Changing_the_password_lets_the_new_one_log_in_and_retires_the_old_one()
    {
        await AuthenticateAsync();

        var change = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(ApiFactory.AdminPassword, NewPassword));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var fresh = _factory.CreateClient();

        var withNew = await fresh.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, NewPassword));
        withNew.StatusCode.Should().Be(HttpStatusCode.OK);

        // Both halves matter: a change that accepted the new password while still accepting
        // the old one would pass a weaker test and leave the old credential live.
        var withOld = await fresh.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        withOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_current_password_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest("not-the-current-password", NewPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PASSWORD_INCORRECT");
    }

    [Fact]
    public async Task A_short_new_password_is_rejected()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(ApiFactory.AdminPassword, "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("PASSWORD_TOO_SHORT");
    }

    [Fact]
    public async Task Changing_the_password_clears_the_must_change_flag_and_revokes_refresh_tokens()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
            user.MustChangePassword = true;
            await context.SaveChangesAsync();
        }

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var change = await _client.PostAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(ApiFactory.AdminPassword, NewPassword));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
            user.MustChangePassword.Should().BeFalse();
        }

        // The refresh token issued against the old password must not survive the change:
        // otherwise a stolen refresh token outlives the rotation meant to kill it.
        using var fresh = _factory.CreateClient();
        var refresh = await fresh.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
