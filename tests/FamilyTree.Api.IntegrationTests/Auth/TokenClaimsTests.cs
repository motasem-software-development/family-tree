using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using FamilyTree.Api.IntegrationTests.Fixtures;
using FamilyTree.Application.Auth;
using FamilyTree.Contracts.Auth;
using FamilyTree.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Api.IntegrationTests.Auth;

[Collection("postgres")]
public sealed class TokenClaimsTests(PostgresFixture fixture) : IAsyncLifetime
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

    private async Task<JwtSecurityToken> LoginAndReadTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        return new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
    }

    [Fact]
    public async Task The_seeded_admin_token_carries_no_must_change_password_claim()
    {
        var token = await LoginAndReadTokenAsync();

        // The seeded admin's password came from configuration, not from another administrator,
        // so it is not temporary and must not be forced to change.
        token.Claims.Should().NotContain(c => c.Type == AuthClaims.MustChangePassword);
    }

    [Fact]
    public async Task A_user_flagged_for_password_change_gets_the_claim()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.NormalizedEmail == ApiFactory.AdminEmail.ToUpperInvariant());
            user.MustChangePassword = true;
            await context.SaveChangesAsync();
        }

        var token = await LoginAndReadTokenAsync();

        token.Claims.Should().ContainSingle(c => c.Type == AuthClaims.MustChangePassword)
            .Which.Value.Should().Be("true");
    }
}
