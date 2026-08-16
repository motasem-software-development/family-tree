namespace FamilyTree.Contracts.Auth;

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);
