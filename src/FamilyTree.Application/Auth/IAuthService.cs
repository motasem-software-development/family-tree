using FamilyTree.Contracts.Auth;

namespace FamilyTree.Application.Auth;

public sealed record AuthResult(LoginResponse? Response, string? ErrorCode)
{
    public bool Succeeded => ErrorCode is null;

    public static AuthResult Success(LoginResponse response) => new(response, null);
    public static AuthResult Failure(string errorCode) => new(null, errorCode);
}

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
    Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default);
}
