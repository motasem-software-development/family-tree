using FamilyTree.Api.Middleware;
using FamilyTree.Application.Common;
using Microsoft.AspNetCore.Http;

namespace FamilyTree.Api.IntegrationTests.Fixtures;

/// <summary>
/// The production <see cref="HttpTenantContext"/> with a test-only escape hatch.
///
/// Tests that drive the API over HTTP get exactly the production behaviour: with no override
/// set, every member delegates to the claims-based context. Tests that exercise a service
/// directly from a DI scope have no HttpContext at all, so the claims context would report
/// <see cref="Guid.Empty"/> and every tenant-filtered query would come back empty. Such a test
/// sets the override once, before anything in the scope is resolved, to name the tenant it is
/// acting as — the same value the JWT would have carried.
/// </summary>
public sealed class OverridableTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private readonly HttpTenantContext _http = new(accessor);

    /// <summary>Set before resolving anything else in the scope: ApplicationDbContext reads the tenant once, in its constructor.</summary>
    public Guid? Override { get; set; }

    public Guid TenantId => Override ?? _http.TenantId;

    public Guid UserId => _http.UserId;

    public bool IsAuthenticated => Override is not null || _http.IsAuthenticated;
}
