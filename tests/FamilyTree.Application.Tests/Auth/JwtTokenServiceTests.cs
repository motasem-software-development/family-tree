using System.Security.Claims;
using FluentAssertions;
using FamilyTree.Application.Auth;
using FamilyTree.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace FamilyTree.Application.Tests.Auth;

public class JwtTokenServiceTests
{
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long!!";
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();

    private static JwtTokenService CreateService() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "https://localhost:5001",
            Audience = "familytree-api",
            SigningKey = SigningKey,
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 14
        }), TimeProvider.System);

    private static JsonWebToken Parse(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token);

    [Fact]
    public void CreateAccessToken_embeds_the_user_tenant_and_permissions()
    {
        var token = CreateService().CreateAccessToken(
            UserId, TenantId, "admin@example.com", ["Member.View", "Member.Create"]);

        var jwt = Parse(token.Value);

        jwt.GetClaim(ClaimTypes.NameIdentifier).Value.Should().Be(UserId.ToString());
        jwt.GetClaim("tenant_id").Value.Should().Be(TenantId.ToString());
        jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value)
           .Should().BeEquivalentTo("Member.View", "Member.Create");
    }

    [Fact]
    public void CreateAccessToken_expires_in_fifteen_minutes()
    {
        var token = CreateService().CreateAccessToken(UserId, TenantId, "admin@example.com", []);

        token.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateAccessToken_produces_a_token_that_validates_against_the_signing_key()
    {
        var token = CreateService().CreateAccessToken(UserId, TenantId, "admin@example.com", []);

        var result = new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = "https://localhost:5001",
            ValidAudience = "familytree-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateIssuerSigningKey = true
        }).GetAwaiter().GetResult();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateRefreshToken_returns_a_raw_token_and_its_hash_which_differ()
    {
        var pair = CreateService().CreateRefreshToken();

        pair.RawToken.Should().NotBeNullOrWhiteSpace();
        pair.TokenHash.Should().NotBeNullOrWhiteSpace();
        pair.TokenHash.Should().NotBe(pair.RawToken, "only the hash is ever persisted");
    }

    [Fact]
    public void CreateRefreshToken_never_repeats_a_value()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 100).Select(_ => service.CreateRefreshToken().RawToken).ToArray();

        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void HashRefreshToken_is_deterministic_so_a_presented_token_can_be_looked_up()
    {
        var service = CreateService();
        var pair = service.CreateRefreshToken();

        service.HashRefreshToken(pair.RawToken).Should().Be(pair.TokenHash);
    }
}
