using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class UpcomingCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 22);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(
        string name, DateOnly? born = null, DateOnly? died = null, bool deceased = false) =>
        FamilyMember.Create(TenantId, TreeId, null, name, Now, born, died, deceased);

    private static UpcomingReport Calculate(params FamilyMember[] members) =>
        UpcomingCalculator.Calculate(members, Today);

    [Fact]
    public void An_empty_tree_has_nothing_upcoming()
    {
        var report = Calculate();

        report.Birthdays.Should().BeEmpty();
        report.Anniversaries.Should().BeEmpty();
        report.WindowDays.Should().Be(ReportLimits.UpcomingWindowDays);
    }

    [Fact]
    public void A_birthday_inside_the_window_is_listed_with_its_distance_and_new_age()
    {
        var report = Calculate(Member("عمر", born: new DateOnly(1990, 9, 1)));

        var birthday = report.Birthdays.Should().ContainSingle().Subject;
        birthday.Occurrence.Should().Be(new DateOnly(2026, 9, 1));
        birthday.DaysAway.Should().Be(10);
        birthday.TurningAge.Should().Be(36);
    }

    [Fact]
    public void A_birthday_beyond_the_window_is_omitted()
    {
        Calculate(Member("عمر", born: new DateOnly(1990, 11, 1))).Birthdays.Should().BeEmpty();
    }

    [Fact]
    public void A_birthday_falling_today_is_included_at_zero_days_away()
    {
        var report = Calculate(Member("عمر", born: new DateOnly(1990, 8, 22)));

        report.Birthdays.Should().ContainSingle().Which.DaysAway.Should().Be(0);
    }

    /// <summary>The window is inclusive at its far edge.</summary>
    [Fact]
    public void A_birthday_on_the_last_day_of_the_window_is_included()
    {
        var report = Calculate(Member("عمر", born: new DateOnly(1990, 9, 21)));

        report.Birthdays.Should().ContainSingle().Which.DaysAway.Should().Be(30);
    }

    [Fact]
    public void A_birthday_one_day_past_the_window_is_omitted()
    {
        Calculate(Member("عمر", born: new DateOnly(1990, 9, 22))).Birthdays.Should().BeEmpty();
    }

    /// <summary>A birthday list including the dead is a bug, not a feature.</summary>
    [Fact]
    public void A_deceased_members_birthday_is_not_listed()
    {
        var member = Member("سليمان", born: new DateOnly(1900, 9, 1), deceased: true);

        Calculate(member).Birthdays.Should().BeEmpty();
    }

    [Fact]
    public void A_death_anniversary_inside_the_window_is_listed()
    {
        var member = Member(
            "سليمان", born: new DateOnly(1900, 1, 1), died: new DateOnly(1980, 9, 1));

        var anniversary = Calculate(member).Anniversaries.Should().ContainSingle().Subject;
        anniversary.Occurrence.Should().Be(new DateOnly(2026, 9, 1));
        anniversary.Years.Should().Be(46);
    }

    /// <summary>
    /// The flag alone is not enough: the domain allows a death with no date, and those members
    /// belong in the completeness report, not given an invented anniversary.
    /// </summary>
    [Fact]
    public void A_deceased_member_without_a_death_date_has_no_anniversary()
    {
        Calculate(Member("سليمان", deceased: true)).Anniversaries.Should().BeEmpty();
    }

    [Fact]
    public void Birthdays_are_ordered_by_how_soon_they_fall()
    {
        var report = Calculate(
            Member("خالد", born: new DateOnly(1990, 9, 10)),
            Member("عمر", born: new DateOnly(1990, 8, 25)));

        report.Birthdays.Select(b => b.Member.Name).Should().ContainInOrder("عمر", "خالد");
    }

    /// <summary>The year-boundary case, end to end through the calculator.</summary>
    [Fact]
    public void A_january_birthday_is_reached_from_a_december_reference_day()
    {
        var report = UpcomingCalculator.Calculate(
            [Member("عمر", born: new DateOnly(1990, 1, 5))], new DateOnly(2026, 12, 20));

        var birthday = report.Birthdays.Should().ContainSingle().Subject;
        birthday.Occurrence.Should().Be(new DateOnly(2027, 1, 5));
        birthday.DaysAway.Should().Be(16);
        birthday.TurningAge.Should().Be(37);
    }

    /// <summary>Design §5: the upcoming lists are capped too, and disclose it.</summary>
    [Fact]
    public void A_birthday_list_longer_than_the_cap_is_truncated_but_keeps_its_true_count()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList + 1)
            .Select(i => Member($"عضو {i}", born: new DateOnly(1990, 8, 23)))
            .ToArray();

        var report = Calculate(members);

        report.BirthdayCount.Should().Be(ReportLimits.MaxMembersPerList + 1);
        report.Birthdays.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }
}
