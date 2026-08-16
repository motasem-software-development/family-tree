using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FamilyTree.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FamilyTree.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    public const string TenantIdClaim = "tenant_id";
    public const string PermissionClaim = "permission";

    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(
        Guid userId, Guid tenantId, string email, IReadOnlyCollection<string> permissions)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(TenantIdClaim, tenantId.ToString())
        };
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaim, p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public RefreshTokenPair CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new RefreshTokenPair(raw, HashRefreshToken(raw));
    }

    /// <summary>
    /// SHA-256, not a password hash. The token is 256 bits of cryptographic randomness, so it
    /// is not brute-forceable and needs no work factor — but hashing still means a database
    /// leak yields no usable tokens. It must stay deterministic so a presented token is findable.
    /// </summary>
    public string HashRefreshToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
