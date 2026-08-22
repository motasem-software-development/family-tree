using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.Reports;

public static class UpcomingCalculator
{
    public static UpcomingReport Calculate(IReadOnlyList<FamilyMember> members, DateOnly today)
    {
        // Birthdays are for the living only. Anniversaries need an actual date, not merely the
        // deceased flag — see the completeness report for members who have one without the other.
        var birthdays = members
            .Where(m => !m.IsDeceased && m.DateOfBirth is not null)
            .Select(m => Observance(m, m.DateOfBirth!.Value, today))
            .Where(o => o is not null)
            .Select(o => o!.Value)
            .OrderBy(o => o.DaysAway)
            .ThenBy(o => o.Member.Name, StringComparer.Ordinal)
            .ToList();

        var anniversaries = members
            .Where(m => m.DateOfDeath is not null)
            .Select(m => Observance(m, m.DateOfDeath!.Value, today))
            .Where(o => o is not null)
            .Select(o => o!.Value)
            .OrderBy(o => o.DaysAway)
            .ThenBy(o => o.Member.Name, StringComparer.Ordinal)
            .ToList();

        return new UpcomingReport(
            WindowDays: ReportLimits.UpcomingWindowDays,
            BirthdayCount: birthdays.Count,
            AnniversaryCount: anniversaries.Count,
            Birthdays: birthdays
                .Take(ReportLimits.MaxMembersPerList)
                .Select(o => new UpcomingBirthday(
                    o.Member, o.Anniversary, o.Occurrence, o.DaysAway, o.Years))
                .ToList(),
            Anniversaries: anniversaries
                .Take(ReportLimits.MaxMembersPerList)
                .Select(o => new UpcomingAnniversary(
                    o.Member, o.Anniversary, o.Occurrence, o.DaysAway, o.Years))
                .ToList());
    }

    private readonly record struct Observed(
        MemberRef Member, DateOnly Anniversary, DateOnly Occurrence, int DaysAway, int Years);

    /// <summary>
    /// Null when the next occurrence falls outside the window. The window is inclusive at both
    /// ends: today counts, and so does the thirtieth day.
    /// </summary>
    private static Observed? Observance(FamilyMember member, DateOnly anniversary, DateOnly today)
    {
        var occurrence = AnniversaryOccurrence.Next(anniversary, today);
        var daysAway = occurrence.DayNumber - today.DayNumber;

        if (daysAway > ReportLimits.UpcomingWindowDays) return null;

        return new Observed(
            MemberRefs.From(member),
            anniversary,
            occurrence,
            daysAway,
            // The age or count reached ON the occurrence, not today's: a list headed "upcoming"
            // showing today's age would be off by one for every entry in it.
            Ages.YearsBetween(anniversary, occurrence));
    }
}
