namespace FamilyTree.Application.Common;

/// <summary>
/// The tenant and user for the current request, resolved server-side from the authenticated
/// principal. Never populated from a header, query string, or route value — see spec §2.3.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
