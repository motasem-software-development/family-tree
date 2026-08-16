namespace FamilyTree.Domain.Common;

/// <summary>Marks an entity scoped to a single tenant, which must carry a global query filter.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
