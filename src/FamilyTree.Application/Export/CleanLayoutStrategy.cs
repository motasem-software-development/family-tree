using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// A designed single-direction layout (design §1.1): root on the leading edge, generations in
/// aligned columns, elbows throughout. Shares passes 2, 3 and 5 with the xmind style and
/// differs only in never splitting the tree across two sides — which is what lets the two
/// styles stay additive rather than becoming separate engines.
/// </summary>
public sealed class CleanLayoutStrategy : ILayoutStrategy
{
    public string Name => "clean";

    public TreeScene Build(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure)
    {
        if (roots.Count == 0) return new TreeScene([], [], new SceneBounds(0, 0, 0, 0));

        var metrics = options.Metrics;
        var packed = VerticalPacking.Pack(roots, metrics, measure);

        var nodes = new List<SceneNode>();
        var connectors = new List<SceneConnector>();
        var cursorY = 0.0;

        // VerticalPacking restarts BranchIndex at 0 for every root, so in a forest each root's
        // first branch would otherwise repeat the previous root's first hue. Xmind avoids this
        // by wrapping a forest in a synthetic centre; clean has no centre, so it carries the
        // offset itself and hue keeps identifying the branch across the whole sheet.
        var hueOffset = 0;

        foreach (var root in packed)
        {
            root.Shift(cursorY - root.Top);
            ColumnAssignment.Assign(
                root, startX: 0, direction: 1, metrics, ColumnAlignment.Trailing);
            cursorY = root.Bottom + metrics.SiblingGroupGap;

            foreach (var node in root.Descend())
            {
                // Depth 0 is the root itself; everything below it wears its branch's hue.
                var color = node.Depth == 0
                    ? options.Palette.CentreColor
                    : options.Palette.ColorAt(node.BranchIndex + hueOffset);

                var fontSize = metrics.FontSizeForDepth(node.Depth);

                nodes.Add(new SceneNode(
                    node.Source.Id, node.Source.Name, node.X, node.Y, node.Width,
                    fontSize * 1.6, fontSize, color, metrics.ShapeForDepth(node.Depth)));

                connectors.Add(ConnectorBuilder.Tick(
                    new ScenePoint(node.X, node.Y),
                    new ScenePoint(node.X + node.Width, node.Y),
                    color, metrics.ConnectorStroke));

                foreach (var child in node.Children)
                    connectors.Add(ConnectorBuilder.Elbow(
                        new ScenePoint(node.X + node.Width, node.Y),
                        new ScenePoint(child.X, child.Y),
                        junctionX: node.X + node.Width + metrics.ColumnGap / 2,
                        options.Palette.ColorAt(child.BranchIndex + hueOffset),
                        metrics.ConnectorStroke));
            }

            hueOffset += root.Children.Count;
        }

        return SceneNormaliser.Normalise(nodes, connectors, metrics);
    }
}
