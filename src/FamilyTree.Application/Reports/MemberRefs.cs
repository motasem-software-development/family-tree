using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

/// <summary>
/// Maps a domain member to its report row. Lives in Application rather than on MemberRef
/// itself because Contracts deliberately references no other project — a contract record
/// cannot see a Domain type without inverting the dependency rule.
/// </summary>
public static class MemberRefs
{
    public static MemberRef From(FamilyMember member) =>
        new(member.Id, member.Name, member.ParentId);
}
