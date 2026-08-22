using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class CompletenessCalculator
{
    public static CompletenessReport Calculate(IReadOnlyList<FamilyMember> members)
    {
        var issues = new List<CompletenessIssue>
        {
            IssueFor(CompletenessCodes.MissingBirthDate, members, MissingBirthDate),
            IssueFor(CompletenessCodes.DeceasedWithoutDeathDate, members, DeceasedWithoutDeathDate)
        };

        return new CompletenessReport(
            TotalMembers: members.Count,
            CompleteRecords: members.Count(m => !MissingBirthDate(m) && !DeceasedWithoutDeathDate(m)),
            Issues: issues);
    }

    private static bool MissingBirthDate(FamilyMember member) => member.DateOfBirth is null;

    /// <summary>
    /// The flag with no date. Genealogy routinely establishes that someone died while the date
    /// itself is lost, which is exactly the record a curator needs to chase.
    /// </summary>
    private static bool DeceasedWithoutDeathDate(FamilyMember member) =>
        member.IsDeceased && member.DateOfDeath is null;

    /// <summary>
    /// Emitted even at zero, so the screen renders a stable set of rows rather than one that
    /// appears and disappears as the data is corrected.
    /// </summary>
    private static CompletenessIssue IssueFor(
        string code, IReadOnlyList<FamilyMember> members, Func<FamilyMember, bool> predicate)
    {
        var affected = members.Where(predicate).ToList();

        return new CompletenessIssue(
            code,
            affected.Count,
            affected
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Take(ReportLimits.MaxMembersPerList)
                .Select(MemberRefs.From)
                .ToList());
    }
}
