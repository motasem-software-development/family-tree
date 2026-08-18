using FamilyTree.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace FamilyTree.Infrastructure.Identity;

/// <summary>
/// Identity supplies the credential store. Roles are NOT Identity roles — they are
/// tenant-scoped and permission-backed, which Identity's global roles cannot express.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, ITenantOwned
{
    public Guid TenantId { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Set when an administrator chooses the password (create or reset). While set, the access
    /// token carries a claim that blocks every route but GET /me and POST /me/password
    /// (design spec §4.9). Self-service change clears it.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
