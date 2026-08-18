using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class ColumnAssignmentTests
{
    private static readonly LayoutMetrics Metrics = new();

    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static PackedNode Packed(FamilyTreeNodeResponse root) =>
        VerticalPacking.Pack([root], Metrics, Stub).Single();

    [Fact]
    public void Every_node_at_the_same_depth_shares_one_column()
    {
        var root = Packed(Node("r", Node("aaaa", Node("x")), Node("b", Node("yyyyyy"))));
        ColumnAssignment.Assign(root, startX: 0, direction: 1, Metrics);

        root.Children[0].X.Should().Be(root.Children[1].X);
        root.Children[0].Children[0].X.Should().Be(root.Children[1].Children[0].X);
    }

    [Fact]
    public void A_column_is_as_wide_as_its_widest_label_plus_the_gap()
    {
        var root = Packed(Node("r", Node("aaaa", Node("x")), Node("b", Node("yyyyyy"))));
        ColumnAssignment.Assign(root, startX: 0, direction: 1, Metrics);

        var widestAtDepthOne = Math.Max(root.Children[0].Width, root.Children[1].Width);
        var pitch = root.Children[0].Children[0].X - root.Children[0].X;

        pitch.Should().BeApproximately(widestAtDepthOne + Metrics.ColumnGap, 1e-9);
    }

    [Fact]
    public void A_leftward_branch_mirrors_a_rightward_one()
    {
        var right = Packed(Node("r", Node("a", Node("x"))));
        var left = Packed(Node("r", Node("a", Node("x"))));

        ColumnAssignment.Assign(right, startX: 0, direction: 1, Metrics);
        ColumnAssignment.Assign(left, startX: 0, direction: -1, Metrics);

        // Mirrored about startX: the left branch's node right edge lands where the right
        // branch's left edge does, reflected.
        var rightChild = right.Children[0].Children[0];
        var leftChild = left.Children[0].Children[0];

        (leftChild.X + leftChild.Width).Should().BeApproximately(-rightChild.X, 1e-9);
    }

    [Fact]
    public void The_returned_extent_is_the_branch_outer_edge()
    {
        var root = Packed(Node("r", Node("a", Node("x"))));
        var extent = ColumnAssignment.Assign(root, startX: 0, direction: 1, Metrics);

        var deepest = root.Children[0].Children[0];
        extent.Should().BeApproximately(deepest.X + deepest.Width, 1e-9);
    }
}
