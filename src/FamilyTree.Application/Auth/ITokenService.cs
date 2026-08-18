namespace FamilyTree.Application.Auth;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>The raw token goes to the client exactly once; only the hash is persisted.</summary>
public sealed record RefreshTokenPair(string RawToken, string TokenHash);

public interface ITokenService
{
    AccessToken CreateAccessToken(
        Guid userId, Guid tenantId, string email,
        IReadOnlyCollection<string> permissions, bool mustChangePassword);

    RefreshTokenPair CreateRefreshToken();

    string HashRefreshToken(string rawToken);
}
