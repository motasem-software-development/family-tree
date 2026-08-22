using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class ActivityCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    /// <summary>Created at a chosen moment, so a member can be placed inside or outside the window.</summary>
    private static FamilyMember MemberCreatedAt(string name, DateTimeOffset createdAt) =>
        FamilyMember.Create(TenantId, TreeId, null, name, createdAt);

    private static ActivityReport Calculate(params FamilyMember[] members) =>
        ActivityCalculator.Calculate(members, Now);

    [Fact]
    public void An_empty_tree_has_no_activity()
    {
        var report = Calculate();

        report.Added.Should().BeEmpty();
        report.Edited.Should().BeEmpty();
        report.WindowDays.Should().Be(ReportLimits.ActivityWindowDays);
    }

    [Fact]
    public void A_member_created_inside_the_window_is_listed_as_added()
    {
        var report = Calculate(MemberCreatedAt("عمر", Now.AddDays(-3)));

        report.Added.Should().ContainSingle().Which.Member.Name.Should().Be("عمر");
        report.Edited.Should().BeEmpty();
    }

    [Fact]
    public void A_member_created_before_the_window_is_not_listed_as_added()
    {
        Calculate(MemberCreatedAt("عمر", Now.AddDays(-40))).Added.Should().BeEmpty();
    }

    [Fact]
    public void An_edit_to_a_member_that_already_existed_is_listed_as_edited()
    {
        var member = MemberCreatedAt("عمر", Now.AddDays(-40));
        member.Rename("عمر", Now.AddDays(-2));

        var report = Calculate(member);

        report.Edited.Should().ContainSingle().Which.Member.Name.Should().Be("عمر");
        report.Added.Should().BeEmpty();
    }

    /// <summary>
    /// Design §6. Testing UpdatedAt != CreatedAt instead would list this member twice in the
    /// same week's report; the arrival is the more informative fact, so Added wins.
    /// </summary>
    [Fact]
    public void A_member_added_and_edited_inside_the_window_appears_once_under_added()
    {
        var member = MemberCreatedAt("عمر", Now.AddDays(-5));
        member.Rename("عمر", Now.AddDays(-1));

        var report = Calculate(member);

        report.Added.Should().ContainSingle();
        report.Edited.Should().BeEmpty();
    }

    [Fact]
    public void An_untouched_old_member_appears_in_neither_list()
    {
        var report = Calculate(MemberCreatedAt("سليمان", Now.AddDays(-400)));

        report.Added.Should().BeEmpty();
        report.Edited.Should().BeEmpty();
    }

    [Fact]
    public void The_most_recent_change_is_listed_first()
    {
        var report = Calculate(
            MemberCreatedAt("خالد", Now.AddDays(-10)),
            MemberCreatedAt("عمر", Now.AddDays(-1)));

        report.Added.Select(e => e.Member.Name).Should().ContainInOrder("عمر", "خالد");
    }

    [Fact]
    public void An_added_list_longer_than_the_cap_is_truncated_but_keeps_its_true_count()
    {
        var members = Enumerable.Range(0, ReportLimits.MaxMembersPerList + 1)
            .Select(i => MemberCreatedAt($"عضو {i}", Now.AddDays(-1)))
            .ToArray();

        var report = Calculate(members);

        report.AddedCount.Should().Be(ReportLimits.MaxMembersPerList + 1);
        report.Added.Should().HaveCount(ReportLimits.MaxMembersPerList);
    }
}
