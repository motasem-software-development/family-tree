using FamilyTree.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTree.Api.Errors;

/// <summary>
/// Turns domain rule violations into Problem Details carrying the stable machine-readable
/// code. Message text is never the contract — clients translate from `code` (spec §4.8).
/// </summary>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domainException) return false;

        // The subclass decides the status; a bare DomainException is a request-level rule
        // violation and stays a 400.
        var (status, title) = domainException switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Request conflicts with the current state"),
            TooLargeException => (StatusCodes.Status413PayloadTooLarge, "Result exceeds a hard limit"),
            _ => (StatusCodes.Status400BadRequest, "Request violates a business rule")
        };

        logger.LogWarning("Domain rule violated: {Code}", domainException.Code);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // A 404 must not describe what was missing — the response stays identical whether
            // the id is unknown or owned by another tenant (design spec §4.4).
            Detail = status == StatusCodes.Status404NotFound ? null : domainException.Message,
            Extensions = { ["code"] = domainException.Code }
        };

        // Only some causes have a remedy the caller can act on; the reason is how a client
        // knows whether to offer one (design §5.3).
        if (domainException is TooLargeException tooLarge)
            problem.Extensions["reason"] = tooLarge.Reason;

        httpContext.Response.StatusCode = problem.Status.Value;
        // Match the content type Results.Problem (used by ProblemResults.Coded) produces, so
        // the API never emits two different Problem Details content types (spec §4.8).
        await httpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", ct);
        return true;
    }
}

public static class ProblemResults
{
    /// <summary>Problem Details with a stable `code` extension, used by every failing endpoint.</summary>
    public static IResult Coded(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?>
        {
            ["code"] = code
        });
}
