using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// Replicates the reference's mindmap vocabulary (design §4.3): a centre node with branches
/// balanced across both sides, tapered ribbons to the top level, orthogonal elbows below.
/// </summary>
public sealed class XmindLayoutStrategy : ILayoutStrategy
{
    public string Name => "xmind";

    public TreeScene Build(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutOptions options, MeasureText measure)
    {
        if (roots.Count == 0) return new TreeScene([], [], new SceneBounds(0, 0, 0, 0));

        var metrics = options.Metrics;

        // One stored root becomes the centre. A forest gets a synthetic centre so every root
        // survives as its own coloured branch rather than being dropped. The synthetic node is
        // never emitted, only used to hold the branches.
        var isSynthetic = roots.Count > 1;
        var centreSource = isSynthetic
            ? new FamilyTreeNodeResponse(Guid.Empty, string.Empty, null, 0, false, roots)
            : roots[0];

        var centre = VerticalPacking.Pack([centreSource], metrics, measure).Single();
        if (isSynthetic) centre.Width = 0;

        var nodes = new List<SceneNode>();
        var connectors = new List<SceneConnector>();

        var topLevel = centre.Children;
        if (topLevel.Count == 0)
        {
            if (!isSynthetic) nodes.Add(ToScene(centre, options.Palette.CentreColor, metrics));
            return SceneNormaliser.Normalise(nodes, connectors, metrics);
        }

        var sides = SideAssignment.Assign(topLevel);
        PlaceSides(centre, topLevel, sides, metrics);

        if (!isSynthetic) nodes.Add(ToScene(centre, options.Palette.CentreColor, metrics));

        foreach (var branch in topLevel)
        {
            var color = options.Palette.ColorAt(branch.BranchIndex);
            var direction = sides[branch] == Side.Right ? 1 : -1;

            foreach (var node in branch.Descend())
            {
                nodes.Add(ToScene(node, color, metrics));
                connectors.Add(ConnectorBuilder.Tick(
                    InnerEdge(node, direction), OuterEdge(node, direction),
                    color, metrics.ConnectorStroke));

                foreach (var child in node.Children)
                {
                    var from = OuterEdge(node, direction);
                    connectors.Add(ConnectorBuilder.Elbow(
                        from,
                        InnerEdge(child, direction),
                        junctionX: from.X + direction * metrics.ColumnGap / 2,
                        color,
                        metrics.ConnectorStroke));
                }
            }

            // A synthetic forest centre is invisible, so ribbons radiating from it would imply
            // a common ancestor that does not exist. Real roots get their ribbon; a forest
            // gets none.
            if (!isSynthetic)
                connectors.Add(ConnectorBuilder.Ribbon(
                    new ScenePoint(centre.X + (direction > 0 ? centre.Width : 0), centre.Y),
                    InnerEdge(branch, direction),
                    metrics.RibbonHalfWidth,
                    color));
        }

        return SceneNormaliser.Normalise(nodes, connectors, metrics);
    }

    /// <summary>
    /// Pass 5's placement. Each side's stack is centred on the centre node, which is what puts
    /// the centre at whatever fraction of the page balances the two masses (design §2.2).
    /// </summary>
    private static void PlaceSides(
        PackedNode centre, IReadOnlyList<PackedNode> topLevel,
        IReadOnlyDictionary<PackedNode, Side> sides, LayoutMetrics metrics)
    {
        centre.X = 0;
        centre.Y = 0;

        foreach (var side in new[] { Side.Right, Side.Left })
        {
            var onSide = topLevel.Where(n => sides[n] == side).ToList();
            if (onSide.Count == 0) continue;

            var total = onSide.Sum(n => n.Height) + (onSide.Count - 1) * metrics.SiblingGroupGap;
            var direction = side == Side.Right ? 1 : -1;
            var startX = direction > 0
                ? centre.Width + metrics.ColumnGap * 2
                : -metrics.ColumnGap * 2;

            var cursor = -total / 2;
            // One column table for the whole side: without it each branch sizes its own columns,
            // so two top-level branches whose names differ in width can never line up.
            var columns = ColumnAssignment.WidestByDepth(onSide);

            foreach (var branch in onSide)
            {
                branch.Shift(cursor - branch.Top);
                ColumnAssignment.Assign(
                    branch, startX, direction, metrics, ColumnAlignment.Trailing, columns);
                cursor = branch.Bottom + metrics.SiblingGroupGap;
            }
        }
    }

    private static SceneNode ToScene(PackedNode node, string color, LayoutMetrics metrics)
    {
        var fontSize = metrics.FontSizeForDepth(node.Depth);
        return new SceneNode(
            node.Source.Id,
            node.Source.Name,
            node.X,
            node.Y,
            node.Width,
            fontSize * 1.6,
            fontSize,
            color,
            metrics.ShapeForDepth(node.Depth));
    }

    private static ScenePoint OuterEdge(PackedNode node, int direction) =>
        new(direction > 0 ? node.X + node.Width : node.X, node.Y);

    private static ScenePoint InnerEdge(PackedNode node, int direction) =>
        new(direction > 0 ? node.X : node.X + node.Width, node.Y);
}
