using FamilyTree.Api.Errors;
using FamilyTree.Application.Auth;

namespace FamilyTree.Api.Authorization;

/// <summary>
/// While a user holds a temporary password, their token can do exactly two things: read /me
/// and set a new password. Enforced here rather than in the frontend because §9 forbids
/// business rules that live only in the UI — the same token presented by curl is equally
/// restricted (design spec §4.9).
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    private static readonly (string Method, string Path)[] Allowed =
    [
        ("GET", "/api/v1/me"),
        ("POST", "/api/v1/me/password")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsBlocked(context))
        {
            await next(context);
            return;
        }

        var result = ProblemResults.Coded(
            StatusCodes.Status403Forbidden,
            "PASSWORD_CHANGE_REQUIRED",
            "A password change is required before continuing.");

        await result.ExecuteAsync(context);
    }

    private static bool IsBlocked(HttpContext context)
    {
        // Anonymous requests are the authentication layer's problem, not this gate's:
        // login and refresh must stay reachable for a flagged user.
        if (context.User.Identity?.IsAuthenticated != true) return false;

        if (!context.User.HasClaim(AuthClaims.MustChangePassword, "true")) return false;

        return !Allowed.Any(a =>
            string.Equals(context.Request.Method, a.Method, StringComparison.OrdinalIgnoreCase) &&
            context.Request.Path.Equals(a.Path, StringComparison.OrdinalIgnoreCase));
    }
}
