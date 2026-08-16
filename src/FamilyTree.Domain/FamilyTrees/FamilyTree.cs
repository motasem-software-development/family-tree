using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.FamilyTrees;

/// <summary>
/// The root family. Named <c>FamilyTreeAggregate</c> rather than <c>FamilyTree</c> to avoid
/// colliding with the root namespace. Mapped to the <c>family_trees</c> table.
/// Per BR-003 the root family is not a person and is never a FamilyMember.
/// </summary>
public sealed class FamilyTreeAggregate : Entity, ITenantOwned
{
    public const int MaxNameLength = 200;

    private FamilyTreeAggregate() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static FamilyTreeAggregate Create(Guid tenantId, string name, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("FAMILY_TREE_TENANT_REQUIRED", "A family tree must belong to a tenant.");

        var tree = new FamilyTreeAggregate { TenantId = tenantId, IsActive = true };
        tree.Name = ValidateName(name);
        tree.InitializeTimestamps(now);
        return tree;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = ValidateName(name);
        Touch(now);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("FAMILY_TREE_NAME_REQUIRED", "Family tree name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("FAMILY_TREE_NAME_TOO_LONG", $"Family tree name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
