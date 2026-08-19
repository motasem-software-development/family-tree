using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class CleanLayoutStrategyTests
{
    private static double Stub(string text, double fontSize) => text.Length * fontSize * 0.5;

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static TreeScene Build(params FamilyTreeNodeResponse[] roots) =>
        new CleanLayoutStrategy().Build(roots, LayoutOptions.Default, Stub);

    [Fact]
    public void The_strategy_names_itself_clean()
    {
        new CleanLayoutStrategy().Name.Should().Be("clean");
    }

    [Fact]
    public void Every_member_appears_exactly_once()
    {
        var scene = Build(Node("r", Node("a", Node("a1")), Node("b")));

        scene.Nodes.Should().HaveCount(4);
        scene.Nodes.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    // The clean style is single-direction: nothing may sit on the opposite side of the root.
    [Fact]
    public void Every_branch_grows_the_same_way_from_the_root()
    {
        var scene = Build(Node("r", Node("a"), Node("b"), Node("c")));
        var root = scene.Nodes.Single(n => n.Label == "r");

        scene.Nodes.Where(n => n.Label != "r").Should().OnlyContain(n => n.X > root.X);
    }

    [Fact]
    public void Generations_line_up_in_shared_columns()
    {
        var scene = Build(Node("r", Node("aaaa", Node("x")), Node("b", Node("yyyy"))));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["aaaa"].X.Should().Be(byLabel["b"].X);
        byLabel["x"].X.Should().Be(byLabel["yyyy"].X);
    }

    [Fact]
    public void No_ribbons_are_emitted()
    {
        Build(Node("r", Node("a", Node("a1"))))
            .Connectors.Should().NotContain(c => c.Kind == ConnectorKind.Ribbon);
    }

    [Fact]
    public void Descendants_still_inherit_their_branch_hue()
    {
        var scene = Build(Node("r", Node("a", Node("a1")), Node("b")));
        var byLabel = scene.Nodes.ToDictionary(n => n.Label);

        byLabel["a1"].Color.Should().Be(byLabel["a"].Color);
        byLabel["b"].Color.Should().NotBe(byLabel["a"].Color);
    }

    [Fact]
    public void An_empty_tree_produces_an_empty_scene()
    {
        var scene = new CleanLayoutStrategy().Build([], LayoutOptions.Default, Stub);
        scene.Nodes.Should().BeEmpty();
    }

    // A forest is the only path through the cursorY accumulator, and the only way two branches
    // can collide on one hue. Both are exercised here.
    [Fact]
    public void Separate_roots_neither_overlap_vertically_nor_repeat_a_branch_hue()
    {
        var scene = Build(
            Node("first", Node("a"), Node("b")),
            Node("second", Node("c"), Node("d")));

        SceneNode Find(string name) => scene.Nodes.Single(n => n.Label == name);

        Find("second").Y.Should().BeGreaterThan(
            Find("first").Y, "the second root is stacked below the first");
        scene.Nodes.Max(n => n.Y).Should().Be(Find("d").Y, "no root may overlap the next");

        new[] { Find("a").Color, Find("b").Color, Find("c").Color, Find("d").Color }
            .Should().OnlyHaveUniqueItems("hue identifies the branch across the whole sheet");
    }
}
