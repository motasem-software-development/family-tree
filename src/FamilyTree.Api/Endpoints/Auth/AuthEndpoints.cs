using FamilyTree.Api.Errors;
using FamilyTree.Application.Auth;
using FamilyTree.Contracts.Auth;

namespace FamilyTree.Api.Endpoints.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").AllowAnonymous().WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService auth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "VALIDATION_FAILED", "Email and password are required.");

            var result = await auth.LoginAsync(request, ct);

            return result.Succeeded
                ? Results.Ok(result.Response)
                : ProblemResults.Coded(StatusForCode(result.ErrorCode!), result.ErrorCode!, "Authentication failed.");
        });

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return ProblemResults.Coded(StatusCodes.Status400BadRequest,
                    "VALIDATION_FAILED", "Refresh token is required.");

            var result = await auth.RefreshAsync(request.RefreshToken, ct);

            return result.Succeeded
                ? Results.Ok(result.Response)
                : ProblemResults.Coded(StatusCodes.Status401Unauthorized,
                    result.ErrorCode!, "Authentication failed.");
        });

        group.MapPost("/logout", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken ?? string.Empty, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static int StatusForCode(string code) => code switch
    {
        "ACCOUNT_INACTIVE" or "TENANT_INACTIVE" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status401Unauthorized
    };
}
