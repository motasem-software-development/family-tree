using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class XmindLayoutStrategyTests
{
    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static TreeScene Build(params FamilyTreeNodeResponse[] roots) =>
        new XmindLayoutStrategy().Build(roots, LayoutOptions.Default, Stub);

    [Fact]
    public void Every_member_appears_exactly_once_in_the_scene()
    {
        var scene = Build(Node("r", Node("a", Node("a1"), Node("a2")), Node("b")));

        scene.Nodes.Should().HaveCount(5);
        scene.Nodes.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Descendants_inherit_their_top_level_ancestors_hue()
    {
        var scene = Build(Node("r", Node("a", Node("a1", Node("a2"))), Node("b")));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["a1"].Color.Should().Be(byLabel["a"].Color);
        byLabel["a2"].Color.Should().Be(byLabel["a"].Color);
        byLabel["b"].Color.Should().NotBe(byLabel["a"].Color);
    }

    [Fact]
    public void The_centre_and_its_children_are_boxed_and_everything_deeper_is_a_tick()
    {
        var scene = Build(Node("r", Node("a", Node("a1"))));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["r"].Shape.Should().Be(NodeShape.RoundedBox);
        byLabel["a"].Shape.Should().Be(NodeShape.RoundedBox);
        byLabel["a1"].Shape.Should().Be(NodeShape.Tick);
    }

    [Fact]
    public void The_centre_uses_the_reserved_centre_colour()
    {
        var scene = Build(Node("r", Node("a")));
        scene.Nodes.Single(n => n.Label == "r").Color
            .Should().Be(BranchPalette.Default.CentreColor);
    }

    [Fact]
    public void Centre_to_level_one_links_are_ribbons_and_deeper_links_are_elbows()
    {
        var scene = Build(Node("r", Node("a", Node("a1"))));

        scene.Connectors.Count(c => c.Kind == ConnectorKind.Ribbon).Should().Be(1);
        // A genuine parent-to-child elbow has 4 points; Tick also emits ConnectorKind.Elbow but
        // with only 2, so this must require the 4-point shape to be discriminating.
        scene.Connectors.Should().Contain(c => c.Kind == ConnectorKind.Elbow && c.Points.Count == 4);
    }

    [Fact]
    public void The_centre_sits_where_its_own_ribbons_anchor()
    {
        var scene = Build(Node("r", Node("a", Node("a1"), Node("a2")), Node("b")));
        var centre = scene.Nodes.Single(n => n.Label == "r");

        var ribbons = scene.Connectors.Where(c => c.Kind == ConnectorKind.Ribbon).ToList();
        ribbons.Should().NotBeEmpty();
        // A ribbon's first and last points are the two edges of the centre-side opening,
        // straddling the centre's Y; their midpoint is where it anchors.
        foreach (var ribbon in ribbons)
            ((ribbon.Points[0].Y + ribbon.Points[^1].Y) / 2)
                .Should().BeApproximately(centre.Y, 1e-6);
    }

    [Fact]
    public void A_ribbon_carries_the_eight_points_of_a_closed_teardrop()
    {
        var scene = Build(Node("r", Node("a")));
        scene.Connectors.Single(c => c.Kind == ConnectorKind.Ribbon).Points.Should().HaveCount(8);
    }

    [Fact]
    public void Branches_land_on_both_sides_of_the_centre()
    {
        var scene = Build(Node("r", Node("a"), Node("b")));
        var centre = scene.Nodes.Single(n => n.Label == "r");

        scene.Nodes.Where(n => n.Label is "a" or "b")
            .Select(n => n.X > centre.X)
            .Should().OnlyHaveUniqueItems("one branch goes right and the other left");
    }

    [Fact]
    public void The_scene_is_normalised_to_the_origin()
    {
        var scene = Build(Node("r", Node("a", Node("a1")), Node("b")));

        scene.Bounds.MinX.Should().Be(0);
        scene.Bounds.MinY.Should().Be(0);
        scene.Nodes.Select(n => n.X).Min().Should().BeGreaterThanOrEqualTo(0);
        scene.Bounds.Width.Should().BeGreaterThan(0);
    }

    // Design §4.3: the API returns RootMembers as a collection, and no root may be silently
    // dropped just because the tree is a forest.
    [Fact]
    public void A_forest_gets_a_synthetic_centre_and_keeps_every_root()
    {
        var scene = Build(Node("one", Node("x")), Node("two", Node("y")));

        scene.Nodes.Select(n => n.Label).Should().Contain(["one", "two", "x", "y"]);
        scene.Nodes.Should().NotContain(n => n.Label == string.Empty);
        scene.Nodes.Single(n => n.Label == "one").Color
            .Should().NotBe(scene.Nodes.Single(n => n.Label == "two").Color);
        // A synthetic centre is invisible; ribbons radiating from it would imply a common
        // ancestor that does not exist, so a forest must have none at all.
        scene.Connectors.Should().NotContain(c => c.Kind == ConnectorKind.Ribbon);
    }

    [Fact]
    public void An_empty_tree_produces_an_empty_scene_rather_than_throwing()
    {
        var scene = new XmindLayoutStrategy().Build([], LayoutOptions.Default, Stub);

        scene.Nodes.Should().BeEmpty();
        scene.Connectors.Should().BeEmpty();
        scene.Bounds.Width.Should().Be(0);
    }
}
