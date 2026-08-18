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
}
