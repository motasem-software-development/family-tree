using System.Security.Claims;
using FamilyTree.Application.Common;

namespace FamilyTree.Api.Middleware;

/// <summary>
/// Reads tenant and user identity from the authenticated principal's claims only.
/// Anything the client can set — headers, query strings, route values — is ignored by design.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public const string TenantIdClaim = "tenant_id";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid TenantId =>
        Guid.TryParse(Principal?.FindFirstValue(TenantIdClaim), out var id) ? id : Guid.Empty;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
