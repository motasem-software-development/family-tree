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

        logger.LogWarning("Domain rule violated: {Code}", domainException.Code);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request violates a business rule",
            Detail = domainException.Message,
            Extensions = { ["code"] = domainException.Code }
        };

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);
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
