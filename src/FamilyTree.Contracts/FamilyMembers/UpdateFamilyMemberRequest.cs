namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Updates a member's name and life details. Every field is applied in one write and costs one
/// version bump, so a single form submission does not leave the client's returned version stale
/// against its own edit.
///
/// The life details are replace-semantics, not patch-semantics: omitting a date clears it. That
/// is what makes correcting a mistaken death record possible. <paramref name="Version"/> is the value from the last read and is
/// required — omitting it is a stale write by definition.
///
/// The three trailing properties exist ONLY so the API can reject them explicitly. Design
/// spec §4.6 requires that an attempt to change parentId, tenantId, or familyTreeId fail
/// loudly rather than be silently dropped; a client that believed it had re-parented a member
/// would corrupt the operator's mental model of the tree. Re-parenting goes through the
/// dedicated move command instead: POST /api/v1/family-members/{id}/move.
/// </summary>
public sealed record UpdateFamilyMemberRequest(
    string Name,
    int Version,
    Guid? ParentId = null,
    Guid? TenantId = null,
    Guid? FamilyTreeId = null,
    DateOnly? DateOfBirth = null,
    DateOnly? DateOfDeath = null,
    bool IsDeceased = false);
