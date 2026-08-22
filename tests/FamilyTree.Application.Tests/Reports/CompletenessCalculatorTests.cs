using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class CompletenessCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(
        string name, DateOnly? born = null, DateOnly? died = null, bool deceased = false) =>
        FamilyMember.Create(TenantId, TreeId, null, name, Now, born, died, deceased);

    private static CompletenessIssue Issue(CompletenessReport report, string code) =>
        report.Issues.Single(i => i.Code == code);

    [Fact]
    public void An_empty_tree_reports_no_issues_and_no_complete_records()
    {
        var report = CompletenessCalculator.Calculate([]);

        report.TotalMembers.Should().Be(0);
        report.CompleteRecords.Should().Be(0);
        report.Issues.Should().OnlyContain(i => i.Count == 0);
    }

    [Fact]
    public void A_member_without_a_birth_date_is_listed()
    {
        var report = CompletenessCalculator.Calculate([Member("سليمان")]);

        var issue = Issue(report, CompletenessCodes.MissingBirthDate);
        issue.Count.Should().Be(1);
        issue.Members.Should().ContainSingle().Which.Name.Should().Be("سليمان");
    }

    [Fact]
    public void A_member_known_to_have_died_without_a_date_is_listed()
    {
        var report = CompletenessCalculator.Calculate(
            [Member("سليمان", born: new DateOnly(1900, 1, 1), deceased: true)]);

        Issue(report, CompletenessCodes.DeceasedWithoutDeathDate).Count.Should().Be(1);
    }

    /// <summary>Setting a death date implies the flag, so this member is not an issue.</summary>
    [Fact]
    public void A_deceased_member_holding_a_death_date_is_not_listed()
    {
        var report = CompletenessCalculator.Calculate(
            [Member("سليمان", born: new DateOnly(1900, 1, 1), died: new DateOnly(1980, 1, 1))]);

        Issue(report, CompletenessCodes.DeceasedWithoutDeathDate).Count.Should().Be(0);
        report.CompleteRecords.Should().Be(1);
    }

    /// <summary>The codes are independent worklists, not a partition of the members.</summary>
    [Fact]
    public void A_member_can_appear_under_more_than_one_code()
    {
        var report = CompletenessCalculator.Calculate([Member("سليمان", deceased: true)]);

        Issue(report, CompletenessCodes.MissingBirthDate).Count.Should().Be(1);
        Issue(report, CompletenessCodes.DeceasedWithoutDeathDate).Count.Should().Be(1);
        report.CompleteRecords.Should().Be(0);
    }

    [Fact]
    public void A_living_member_with_a_birth_date_is_complete()
    {
        var report = CompletenessCalculator.Calculate([Member("عمر", born: new DateOnly(1990, 1, 1))]);

        report.CompleteRecords.Should().Be(1);
    }

    /// <summary>Design §5: the true count survives truncation, so a client cannot under-report.</summary>
    [Fact]
    public void A_list_longer_than_the_cap_is_truncated_but_keeps_its_true_count()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList + 1)
            .Select(i => Member($"عضو {i}"))
            .ToList();

        var issue = Issue(
            CompletenessCalculator.Calculate(members), CompletenessCodes.MissingBirthDate);

        issue.Count.Should().Be(ReportLimits.MaxMembersPerList + 1);
        issue.Members.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }

    [Fact]
    public void A_list_exactly_at_the_cap_is_not_truncated()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList)
            .Select(i => Member($"عضو {i}"))
            .ToList();

        Issue(CompletenessCalculator.Calculate(members), CompletenessCodes.MissingBirthDate)
            .Members.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }

    /// <summary>Every code is always present, so a client renders a stable set of rows.</summary>
    [Fact]
    public void Both_codes_are_returned_even_when_no_member_is_affected()
    {
        var report = CompletenessCalculator.Calculate([Member("عمر", born: new DateOnly(1990, 1, 1))]);

        report.Issues.Select(i => i.Code).Should().BeEquivalentTo(
            [CompletenessCodes.MissingBirthDate, CompletenessCodes.DeceasedWithoutDeathDate],
            options => options.WithStrictOrdering());
    }
}
