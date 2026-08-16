using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// A person in the family hierarchy. Per BR-003 the root family is NOT a member — it is the
/// <c>family_trees</c> row — so a first-generation member has <c>ParentId = null</c>
/// (technical specification §10).
/// </summary>
public sealed class FamilyMember : Entity, ITenantOwned
{
    public const int MaxNameLength = 200;

    private FamilyMember() { }

    public Guid TenantId { get; private set; }
    public Guid FamilyTreeId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Application-managed optimistic concurrency token (design spec §3.1). Mapped as an EF
    /// concurrency token, so a stale update fails loudly instead of silently overwriting a
    /// concurrent edit (technical specification §43).
    /// </summary>
    public int Version { get; private set; }

    public static FamilyMember Create(
        Guid tenantId, Guid familyTreeId, Guid? parentId, string name, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("MEMBER_TENANT_REQUIRED", "A family member must belong to a tenant.");
        if (familyTreeId == Guid.Empty)
            throw new DomainException("MEMBER_TREE_REQUIRED", "A family member must belong to a family tree.");

        var member = new FamilyMember
        {
            TenantId = tenantId,
            FamilyTreeId = familyTreeId,
            // Guid.Empty is never a real member id, so treat it as "no parent" rather than
            // letting it reach the database and fail a foreign key at insert time.
            ParentId = parentId == Guid.Empty ? null : parentId,
            Version = 1
        };
        member.Name = ValidateName(name);
        member.InitializeTimestamps(now);
        return member;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        // Validate before mutating: a rejected rename must leave the entity exactly as it was.
        var validated = ValidateName(name);

        Name = validated;
        Version++;
        Touch(now);
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("MEMBER_NAME_REQUIRED", "Member name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException("MEMBER_NAME_TOO_LONG", $"Member name exceeds {MaxNameLength} characters.");
        return trimmed;
    }
}
