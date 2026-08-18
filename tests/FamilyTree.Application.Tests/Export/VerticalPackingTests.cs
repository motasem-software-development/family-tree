using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class VerticalPackingTests
{
    private static readonly LayoutMetrics Metrics = new();

    /// <summary>Fixed-width stub: layout must never depend on a real font (design §4.2).</summary>
    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static PackedNode Pack(FamilyTreeNodeResponse root) =>
        VerticalPacking.Pack([root], Metrics, Stub).Single();

    [Fact]
    public void A_leaf_occupies_one_leaf_pitch()
    {
        Pack(Node("a")).Height.Should().Be(Metrics.LeafPitch);
    }

    [Fact]
    public void Adjacent_leaves_are_one_leaf_pitch_apart()
    {
        var packed = Pack(Node("p", Node("a"), Node("b")));

        var gap = packed.Children[1].Y - packed.Children[0].Y;
        gap.Should().BeApproximately(Metrics.LeafPitch, 1e-9);
    }

    // The reference's rhythm is bimodal: ~15pt between leaves, ~29.5pt where a sibling group
    // begins (design §3.2). A single constant pitch does not reproduce it.
    [Fact]
    public void A_sibling_with_children_earns_the_wider_group_separation()
    {
        var packed = Pack(Node("p", Node("a"), Node("b", Node("b1"))));

        var gap = packed.Children[1].Y - packed.Children[0].Y;
        gap.Should().BeApproximately(Metrics.GroupSeparation, 1e-9);
    }

    [Fact]
    public void A_parent_centres_between_its_first_and_last_child()
    {
        var packed = Pack(Node("p", Node("a"), Node("b"), Node("c")));

        var expected = (packed.Children[0].Y + packed.Children[^1].Y) / 2;
        packed.Y.Should().BeApproximately(expected, 1e-9);
    }

    // The distinguishing rule: with a lopsided subtree the parent must straddle first and last,
    // NOT sit at the mean of all child centres. Those differ here, and the reference uses the
    // former (design §4.3 pass 2).
    [Fact]
    public void A_parent_of_a_lopsided_subtree_straddles_rather_than_averages()
    {
        // Three children, not two: for exactly two the mean and the straddle are equal by
        // definition, so a two-child fixture cannot discriminate the rule at all.
        var packed = Pack(Node("p",
            Node("a", Node("a1"), Node("a2"), Node("a3")),
            Node("b"),
            Node("c")));

        var straddle = (packed.Children[0].Y + packed.Children[^1].Y) / 2;
        var mean = packed.Children.Average(c => c.Y);

        mean.Should().NotBeApproximately(straddle, 1e-6, "the fixture must actually discriminate");
        packed.Y.Should().BeApproximately(straddle, 1e-9);
    }

    [Fact]
    public void A_parent_block_is_as_tall_as_its_children_plus_their_gaps()
    {
        var packed = Pack(Node("p", Node("a"), Node("b"), Node("c")));

        packed.Height.Should().BeApproximately(3 * Metrics.LeafPitch, 1e-9);
    }

    [Fact]
    public void Depth_and_branch_index_are_carried_down_the_tree()
    {
        var roots = VerticalPacking.Pack(
            [Node("root", Node("x", Node("x1")), Node("y"))], Metrics, Stub);

        var root = roots.Single();
        root.Depth.Should().Be(0);
        root.Children[0].Depth.Should().Be(1);
        root.Children[0].BranchIndex.Should().Be(0);
        root.Children[0].Children[0].Depth.Should().Be(2);
        root.Children[0].Children[0].BranchIndex.Should().Be(0);
        root.Children[1].BranchIndex.Should().Be(1);
    }
}
