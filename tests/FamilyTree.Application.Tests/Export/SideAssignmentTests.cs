using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class SideAssignmentTests
{
    private static readonly LayoutMetrics Metrics = new();

    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static FamilyTreeNodeResponse Leaves(string name, int count) =>
        Node(name, Enumerable.Range(0, count).Select(i => Node($"{name}{i}")).ToArray());

    private static IReadOnlyList<PackedNode> TopLevel(params FamilyTreeNodeResponse[] children) =>
        VerticalPacking.Pack([Node("root", children)], Metrics, Stub).Single().Children;

    [Fact]
    public void The_heaviest_branch_goes_opposite_the_rest_when_it_dominates()
    {
        var top = TopLevel(Leaves("big", 40), Leaves("a", 5), Leaves("b", 5), Node("c"));
        var sides = SideAssignment.Assign(top);

        var heavy = top.OrderByDescending(n => n.Height).First();
        top.Where(n => n != heavy).Should().OnlyContain(n => sides[n] != sides[heavy]);
    }

    [Fact]
    public void The_two_sides_end_up_balanced()
    {
        var top = TopLevel(Leaves("a", 20), Leaves("b", 12), Leaves("c", 9), Node("d"));
        var sides = SideAssignment.Assign(top);

        var right = top.Where(n => sides[n] == Side.Right).Sum(n => n.Height);
        var left = top.Where(n => sides[n] == Side.Left).Sum(n => n.Height);

        Math.Min(right, left).Should().BeGreaterThanOrEqualTo(0.8 * Math.Max(right, left));
    }

    [Fact]
    public void Every_branch_is_assigned_exactly_one_side()
    {
        var top = TopLevel(Leaves("a", 20), Leaves("b", 3), Leaves("c", 18), Leaves("d", 2));
        var sides = SideAssignment.Assign(top);

        sides.Should().HaveCount(top.Count);
        top.Should().OnlyContain(node => sides.ContainsKey(node));
    }

    // When branches have equal weight, ties are broken by original order: first branch takes Right,
    // second takes Left. This ensures reproducible layout across runs.
    [Fact]
    public void Equal_weight_branches_break_ties_by_original_order()
    {
        var top = TopLevel(Leaves("a", 5), Leaves("b", 5));
        var sides = SideAssignment.Assign(top);

        sides[top[0]].Should().Be(Side.Right);
        sides[top[1]].Should().Be(Side.Left);
    }

    [Fact]
    public void A_single_branch_takes_the_right_side()
    {
        var top = TopLevel(Leaves("only", 4));
        SideAssignment.Assign(top).Values.Should().AllBeEquivalentTo(Side.Right);
    }
}
