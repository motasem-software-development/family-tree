using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FamilyTree.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FamilyTree.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    public const string TenantIdClaim = AuthClaims.TenantId;
    public const string PermissionClaim = AuthClaims.Permission;
    public const string MustChangePasswordClaim = AuthClaims.MustChangePassword;

    private const int MinimumSigningKeyBytes = 32;

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        if (string.IsNullOrWhiteSpace(_options.Issuer))
        {
            throw new InvalidOperationException("Jwt:Issuer must be configured and non-empty.");
        }

        if (string.IsNullOrWhiteSpace(_options.Audience))
        {
            throw new InvalidOperationException("Jwt:Audience must be configured and non-empty.");
        }

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured and non-empty.");
        }

        if (Encoding.UTF8.GetByteCount(_options.SigningKey) < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinimumSigningKeyBytes} bytes " +
                "(UTF-8 encoded) for HMAC-SHA256 signing.");
        }
    }

    public AccessToken CreateAccessToken(
        Guid userId, Guid tenantId, string email,
        IReadOnlyCollection<string> permissions, bool mustChangePassword)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(TenantIdClaim, tenantId.ToString())
        };
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaim, p)));

        // Emitted only when true. An absent claim and a "false" claim would both have to be
        // handled by the gate middleware; emitting one shape means the gate has one branch.
        if (mustChangePassword)
            claims.Add(new Claim(MustChangePasswordClaim, "true"));

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
