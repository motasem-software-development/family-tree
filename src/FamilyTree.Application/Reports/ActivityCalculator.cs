using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class ActivityCalculator
{
    public static ActivityReport Calculate(IReadOnlyList<FamilyMember> members, DateTimeOffset now)
    {
        var since = now.AddDays(-ReportLimits.ActivityWindowDays);

        var added = members.Where(m => m.CreatedAt >= since).ToList();

        // Anchored on CreatedAt being OUTSIDE the window, not on UpdatedAt != CreatedAt:
        // Entity.InitializeTimestamps sets the two equal, so the weaker test would list a
        // member created on Monday and corrected on Tuesday under both headings. This way
        // "edited" means a change to a member that already existed, and the lists are
        // disjoint by construction (design §6).
        var edited = members.Where(m => m.UpdatedAt >= since && m.CreatedAt < since).ToList();

        return new ActivityReport(
            WindowDays: ReportLimits.ActivityWindowDays,
            AddedCount: added.Count,
            EditedCount: edited.Count,
            Added: Entries(added, m => m.CreatedAt),
            Edited: Entries(edited, m => m.UpdatedAt));
    }

    private static IReadOnlyList<ActivityEntry> Entries(
        IReadOnlyList<FamilyMember> members, Func<FamilyMember, DateTimeOffset> at) =>
        members
            .OrderByDescending(at)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Take(ReportLimits.MaxMembersPerList)
            .Select(m => new ActivityEntry(MemberRefs.From(m), at(m)))
            .ToList();
}
