using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class LifeStatusCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 22);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(
        string name,
        Guid? parentId = null,
        DateOnly? born = null,
        DateOnly? died = null,
        bool deceased = false) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now, born, died, deceased);

    private static LifeStatusReport Calculate(params FamilyMember[] members) =>
        LifeStatusCalculator.Calculate(members, GenerationIndex.Build(members), Today);

    [Fact]
    public void An_empty_tree_reports_nothing_measurable()
    {
        var report = Calculate();

        report.Living.Should().Be(0);
        report.Deceased.Should().Be(0);
        report.Longevity.Should().BeNull();
    }

    [Fact]
    public void Members_split_by_the_deceased_flag()
    {
        var report = Calculate(
            Member("سليمان", deceased: true),
            Member("فارس"),
            Member("عمر"));

        report.Living.Should().Be(2);
        report.Deceased.Should().Be(1);
    }

    /// <summary>
    /// The flag, never `DateOfDeath is not null` — the domain deliberately allows a member
    /// known to have died whose date is lost.
    /// </summary>
    [Fact]
    public void A_deceased_member_without_a_death_date_still_counts_as_deceased()
    {
        Calculate(Member("سليمان", deceased: true)).Deceased.Should().Be(1);
    }

    [Fact]
    public void The_split_is_reported_per_generation()
    {
        var suleiman = Member("سليمان", deceased: true);
        var faris = Member("فارس", suleiman.Id);

        var report = Calculate(suleiman, faris);

        report.ByGeneration.Should().BeEquivalentTo(
            [new GenerationLifeStatus(1, 0, 1), new GenerationLifeStatus(2, 1, 0)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Living_members_are_bracketed_by_age()
    {
        var report = Calculate(
            Member("عمر", born: new DateOnly(2020, 1, 1)),    // 6
            Member("خالد", born: new DateOnly(1990, 1, 1)),   // 36
            Member("داوود", born: new DateOnly(1940, 1, 1))); // 86

        BracketCount(report, "0-17").Should().Be(1);
        BracketCount(report, "30-44").Should().Be(1);
        BracketCount(report, "75+").Should().Be(1);
    }

    /// <summary>Every bracket is present even at zero, so a chart's axis does not move between loads.</summary>
    [Fact]
    public void All_six_brackets_are_always_returned()
    {
        Calculate(Member("عمر")).LivingAges.Select(b => b.Bracket).Should().BeEquivalentTo(
            ["0-17", "18-29", "30-44", "45-59", "60-74", "75+"],
            options => options.WithStrictOrdering());
    }

    /// <summary>The histogram must not imply a population it did not measure.</summary>
    [Fact]
    public void Living_members_without_a_birth_date_are_excluded_from_the_brackets_and_counted_apart()
    {
        var report = Calculate(Member("عمر"), Member("خالد", born: new DateOnly(1990, 1, 1)));

        report.LivingWithoutBirthDate.Should().Be(1);
        report.LivingAges.Sum(b => b.Count).Should().Be(1);
    }

    [Fact]
    public void Longevity_covers_only_deceased_members_holding_both_dates()
    {
        var report = Calculate(
            Member("سليمان", born: new DateOnly(1900, 1, 1), died: new DateOnly(1980, 1, 1)), // 80
            Member("فارس", born: new DateOnly(1910, 1, 1), died: new DateOnly(1960, 1, 1)),   // 50
            Member("عمر", deceased: true),                                                     // no dates
            Member("خالد", born: new DateOnly(1990, 1, 1)));                                   // living

        report.Longevity!.Count.Should().Be(2);
        report.Longevity.MinYears.Should().Be(50);
        report.Longevity.MaxYears.Should().Be(80);
    }

    [Fact]
    public void Longevity_is_null_when_no_deceased_member_has_both_dates()
    {
        Calculate(Member("عمر", deceased: true)).Longevity.Should().BeNull();
    }

    /// <summary>Whole-year counts, so an even population takes the lower middle, not a mean.</summary>
    [Fact]
    public void An_even_longevity_population_takes_the_lower_middle_value()
    {
        var report = Calculate(
            Deceased("سليمان", 40), Deceased("فارس", 50),
            Deceased("عمر", 60), Deceased("خالد", 70));

        report.Longevity!.MedianYears.Should().Be(50);
    }

    [Fact]
    public void An_odd_longevity_population_takes_the_middle_value()
    {
        var report = Calculate(Deceased("سليمان", 40), Deceased("فارس", 50), Deceased("عمر", 60));

        report.Longevity!.MedianYears.Should().Be(50);
    }

    private static FamilyMember Deceased(string name, int years) =>
        Member(name, born: new DateOnly(1900, 1, 1), died: new DateOnly(1900 + years, 1, 1));

    private static int BracketCount(LifeStatusReport report, string bracket) =>
        report.LivingAges.Single(b => b.Bracket == bracket).Count;
}
