using System.Security.Claims;
using FamilyTree.Api.Authorization;
using FamilyTree.Api.Errors;
using FamilyTree.Application.Auth;
using FamilyTree.Application.Common;
using FamilyTree.Contracts.Auth;
using FamilyTree.Infrastructure.Auth;
using FamilyTree.Infrastructure.Identity;
using FamilyTree.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Api.Endpoints.Me;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", async (
            ITenantContext tenant,
            ApplicationDbContext context,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Filtered query: a tenant with no tree of its own simply finds nothing.
            var tree = await context.FamilyTrees.FirstOrDefaultAsync(ct);
            if (tree is null) return Results.NotFound();

            var email = http.User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

            var permissions = http.User.FindAll(JwtTokenService.PermissionClaim)
                .Select(c => c.Value)
                .ToArray();

            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == tenant.UserId, ct);

            return Results.Ok(new CurrentUserResponse(
                tenant.UserId, email, tenant.TenantId, tree.Name, permissions,
                user?.MustChangePassword ?? false));
        })
        // Authentication only, deliberately — never a permission. A user's role set is
        // optional and a custom role need not grant anything in particular, so requiring a
        // permission here makes identity unreadable for accounts that are otherwise perfectly
        // valid: /me 403s, the client has no user, ProtectedRoute redirects to /login, and the
        // sign-in form renders again with no error even though login itself succeeded — an
        // unbreakable loop with no diagnostic. It is worse for a newly created user, who is
        // always flagged for a password change: PasswordChangeGateMiddleware permits exactly
        // GET /api/v1/me and POST /api/v1/me/password, so a /me that can 403 contradicts the
        // gate's own design, and ChangePasswordPage needs the email /me carries to sign back
        // in. Identity is what you must be able to read in order to learn what you may do.
        // The tree name is not a leak: the caller is an authenticated member of that tenant.
        // Do not add .RequirePermission(...) here.
        .RequireAuthorization()
        .WithTags("Me");

        app.MapPost("/api/v1/me/password", async (
            ChangePasswordRequest request,
            ITenantContext tenant,
            ApplicationDbContext context,
            IPasswordHasher<ApplicationUser> passwordHasher,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == tenant.UserId, ct);
            if (user?.PasswordHash is null)
                return Results.Unauthorized();

            var verification = passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, request.CurrentPassword);

            if (verification == PasswordVerificationResult.Failed)
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "PASSWORD_INCORRECT", "The current password is incorrect.");

            if (request.NewPassword.Length < PasswordPolicy.MinimumLength)
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "PASSWORD_TOO_SHORT",
                    $"A password must be at least {PasswordPolicy.MinimumLength} characters.");

            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
            user.MustChangePassword = false;

            // Every refresh token predates the new password, so each one is a credential the
            // user just chose to rotate away from. Revoking them all is what makes "change my
            // password" also mean "sign my other devices out".
            var now = timeProvider.GetUtcNow();
            var tokens = await context.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync(ct);
            foreach (var token in tokens)
                token.Revoke(now, replacedByTokenHash: null);

            await context.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags("Me");

        return app;
    }
}
