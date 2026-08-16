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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
