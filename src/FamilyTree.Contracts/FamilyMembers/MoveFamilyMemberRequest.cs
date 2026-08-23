namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Re-parents a member. A null <paramref name="ParentId"/> promotes them to first generation,
/// attached to the family tree itself rather than to a member (BR-003).
///
/// <paramref name="Version"/> is the value from the last read and is required — omitting it is
/// a stale write by definition. Move is a dedicated command rather than a field on
/// <see cref="UpdateFamilyMemberRequest"/> because it carries a rule no other edit does: the
/// target must not be the member or one of their descendants (design spec §4.6).
/// </summary>
public sealed record MoveFamilyMemberRequest(Guid? ParentId, int Version);
