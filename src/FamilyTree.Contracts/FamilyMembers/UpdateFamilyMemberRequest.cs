namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Renames a member. <paramref name="Version"/> is the value from the last read and is
/// required — omitting it is a stale write by definition.
///
/// The three trailing properties exist ONLY so the API can reject them explicitly. Design
/// spec §4.6 requires that an attempt to change parentId, tenantId, or familyTreeId fail
/// loudly rather than be silently dropped; a client that believed it had re-parented a member
/// would corrupt the operator's mental model of the tree. Re-parenting is the dedicated move
/// command in Phase 5.
/// </summary>
public sealed record UpdateFamilyMemberRequest(
    string Name,
    int Version,
    Guid? ParentId = null,
    Guid? TenantId = null,
    Guid? FamilyTreeId = null);
