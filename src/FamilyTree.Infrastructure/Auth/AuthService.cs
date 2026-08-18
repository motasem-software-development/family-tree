using FamilyTree.Application.Auth;
using FamilyTree.Application.Authorization;
using FamilyTree.Contracts.Auth;
using FamilyTree.Domain.Authentication;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FamilyTree.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext context,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ITokenService tokenService,
    IPermissionResolver permissionResolver,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IAuthService
{
    // A real, well-formed Identity hash for a throwaway password, generated once at class-init
    // time. Used to equalize the timing of the "unknown email" and "wrong password" branches
    // below — see the comment at its use site. Never used to authenticate anything.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "not-a-real-password");

    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();

        // IgnoreQueryFilters: at login time there is no authenticated principal yet, so the
        // tenant filter would exclude every user. This is the one place that is legitimate.
        var user = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user?.PasswordHash is null)
        {
            // Deliberate wasted work: run a full password verification against a dummy hash
            // even though we already know this login will fail. If we returned immediately
            // here instead, an unknown email would respond faster than a known one with a
            // wrong password (which pays the full PBKDF2 cost below), and that timing gap is
            // measurable — an attacker could use it to enumerate which emails have accounts.
            // Do not "optimize" this away; the wasted call is the point.
            passwordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, request.Password);
            return AuthResult.Failure("INVALID_CREDENTIALS");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            return AuthResult.Failure("INVALID_CREDENTIALS");

        if (!user.IsActive)
            return AuthResult.Failure("ACCOUNT_INACTIVE");

        var tenantActive = await context.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive, ct);
        if (!tenantActive)
            return AuthResult.Failure("TENANT_INACTIVE");

        var now = timeProvider.GetUtcNow();
        user.LastLoginAt = now;

        var response = await IssueTokensAsync(user, now, ct);
        await context.SaveChangesAsync(ct);

        return AuthResult.Success(response);
    }

    public async Task<AuthResult> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = tokenService.HashRefreshToken(rawRefreshToken);
        var now = timeProvider.GetUtcNow();

        var stored = await context.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive(now))
            return AuthResult.Failure("INVALID_REFRESH_TOKEN");

        var user = await context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

        if (user is null || !user.IsActive)
            return AuthResult.Failure("INVALID_REFRESH_TOKEN");

        // The refresh endpoint maps every failure to 401 without a reason, so we must not
        // disclose TENANT_INACTIVE here the way LoginAsync does — a generic refresh-token
        // failure is returned instead, but the tenant-active gate itself must still apply,
        // otherwise a deactivated tenant could keep minting fresh access tokens for the
        // full refresh-token lifetime.
        var tenantActive = await context.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive, ct);
        if (!tenantActive)
            return AuthResult.Failure("INVALID_REFRESH_TOKEN");

        var response = await IssueTokensAsync(user, now, ct);

        // Rotation: the old token is revoked and records its successor, so replaying it fails
        // and the chain is auditable.
        stored.Revoke(now, tokenService.HashRefreshToken(response.RefreshToken));

        await context.SaveChangesAsync(ct);
        return AuthResult.Success(response);
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = tokenService.HashRefreshToken(rawRefreshToken);

        var stored = await context.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Silent no-op when unknown: logout must not reveal which tokens exist.
        if (stored is null) return;

        stored.Revoke(timeProvider.GetUtcNow(), replacedByTokenHash: null);
        await context.SaveChangesAsync(ct);
    }

    private async Task<LoginResponse> IssueTokensAsync(
        ApplicationUser user, DateTimeOffset now, CancellationToken ct)
    {
        // Two-argument overload: login has no authenticated principal, so the tenant must be
        // stated explicitly rather than taken from the ambient context.
        var permissions = await permissionResolver.GetPermissionsAsync(user.Id, user.TenantId, ct);

        var access = tokenService.CreateAccessToken(
            user.Id, user.TenantId, user.Email!, permissions, user.MustChangePassword);

        var refresh = tokenService.CreateRefreshToken();

        context.RefreshTokens.Add(RefreshToken.Issue(
            user.Id, user.TenantId, refresh.TokenHash, now,
            TimeSpan.FromDays(_jwt.RefreshTokenLifetimeDays)));

        return new LoginResponse(access.Value, access.ExpiresAt, refresh.RawToken);
    }
}
