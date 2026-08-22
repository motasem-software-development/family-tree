using FamilyTree.Application.Reports;
using FamilyTree.Contracts.Reports;
using FamilyTree.Domain.FamilyMembers;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Reports;

public class StructureCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid TreeId = Guid.CreateVersion7();

    private static FamilyMember Member(string name, Guid? parentId = null) =>
        FamilyMember.Create(TenantId, TreeId, parentId, name, Now);

    private static StructureReport Calculate(params FamilyMember[] members) =>
        StructureCalculator.Calculate(members, GenerationIndex.Build(members));

    /// <summary>سليمان → (فارس → محمود, عمر), plus a separate root داوود.</summary>
    private static FamilyMember[] TwoBranches()
    {
        var suleiman = Member("سليمان");
        var faris = Member("فارس", suleiman.Id);
        var mahmoud = Member("محمود", faris.Id);
        var omar = Member("عمر", suleiman.Id);
        var dawood = Member("داوود");
        return [suleiman, faris, mahmoud, omar, dawood];
    }

    [Fact]
    public void An_empty_tree_reports_zeros_and_no_branches()
    {
        var report = Calculate();

        report.TotalMembers.Should().Be(0);
        report.Depth.Should().Be(0);
        report.Generations.Should().BeEmpty();
        report.Branches.Should().BeEmpty();
        report.AverageChildrenPerParent.Should().Be(0m);
    }

    [Fact]
    public void Depth_is_the_deepest_generation()
    {
        Calculate(TwoBranches()).Depth.Should().Be(3);
    }

    [Fact]
    public void Generations_are_counted_in_order()
    {
        var report = Calculate(TwoBranches());

        report.Generations.Should().BeEquivalentTo(
            [new GenerationCount(1, 2), new GenerationCount(2, 2), new GenerationCount(3, 1)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_branch_counts_every_descendant_and_its_own_depth()
    {
        var report = Calculate(TwoBranches());

        var suleiman = report.Branches.Single(b => b.Name == "سليمان");
        suleiman.DescendantCount.Should().Be(3);
        suleiman.Depth.Should().Be(3);

        var dawood = report.Branches.Single(b => b.Name == "داوود");
        dawood.DescendantCount.Should().Be(0);
        dawood.Depth.Should().Be(1);
    }

    [Fact]
    public void Leaves_and_parents_partition_the_tree()
    {
        var report = Calculate(TwoBranches());

        report.MembersWithChildren.Should().Be(2);   // سليمان, فارس
        report.LeafMembers.Should().Be(3);           // محمود, عمر, داوود
        (report.MembersWithChildren + report.LeafMembers).Should().Be(report.TotalMembers);
    }

    /// <summary>Divided by parents, not by everyone: 3 children across 2 parents.</summary>
    [Fact]
    public void Average_children_counts_only_members_who_have_children()
    {
        Calculate(TwoBranches()).AverageChildrenPerParent.Should().Be(1.5m);
    }

    [Fact]
    public void A_tree_with_no_parents_reports_an_average_of_zero_rather_than_dividing_by_zero()
    {
        Calculate(Member("سليمان")).AverageChildrenPerParent.Should().Be(0m);
    }

    /// <summary>
    /// Design §5 invariants. Generation 1 is exactly the branch roots, and the report counts
    /// the same members the tree screen renders — the two assertions that catch a broken walk.
    /// </summary>
    [Fact]
    public void Generation_one_is_exactly_the_set_of_branches()
    {
        var members = TwoBranches();
        var report = Calculate(members);

        report.Generations[0].Count.Should().Be(report.Branches.Count);
        report.TotalMembers.Should().Be(members.Length);
    }
}
