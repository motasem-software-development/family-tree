using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Authentication;

/// <summary>
/// One row per issued refresh token. Only the hash is stored, never the raw token.
/// Rotation on use means a replaced token records what superseded it, so a replayed
/// old token is detectable.
/// </summary>
public sealed class RefreshToken : Entity, ITenantOwned
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public static RefreshToken Issue(
        Guid userId, Guid tenantId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("REFRESH_TOKEN_HASH_REQUIRED", "Refresh token hash is required.");

        var token = new RefreshToken
        {
            UserId = userId,
            TenantId = tenantId,
            TokenHash = tokenHash,
            ExpiresAt = now + lifetime
        };
        token.InitializeTimestamps(now);
        return token;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, string? replacedByTokenHash)
    {
        if (RevokedAt is not null) return;

        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
        Touch(now);
    }
}
