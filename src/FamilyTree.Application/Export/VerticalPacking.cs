using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// Pass 2 (design §4.3). Bottom-up: gives every node a block height and a vertical centre,
/// relative to the top of its own subtree. Later passes translate the result into page space.
/// </summary>
public static class VerticalPacking
{
    public static IReadOnlyList<PackedNode> Pack(
        IReadOnlyList<FamilyTreeNodeResponse> roots, LayoutMetrics metrics, MeasureText measure) =>
        roots
            .Select((root, index) => Build(root, depth: 0, branchIndex: index, metrics, measure))
            .ToList();

    private static PackedNode Build(
        FamilyTreeNodeResponse source, int depth, int branchIndex,
        LayoutMetrics metrics, MeasureText measure)
    {
        // A top-level child owns its own hue; deeper nodes inherit their ancestor's.
        var children = source.Children
            .Select((child, index) => Build(
                child, depth + 1, depth == 0 ? index : branchIndex, metrics, measure))
            .ToList();

        var node = new PackedNode(source, depth, branchIndex, children)
        {
            Width = measure(source.Name, metrics.FontSizeForDepth(depth)) + metrics.LabelPadding * 2
        };

        if (node.IsLeaf)
        {
            node.Top = 0;
            node.Bottom = metrics.LeafPitch;
            node.Y = metrics.LeafPitch / 2;
            return node;
        }

        StackChildren(children, metrics);

        var first = children[0];
        var last = children[^1];

        node.Top = first.Top;
        node.Bottom = last.Bottom;

        // Straddle first and last rather than averaging every child: with a lopsided subtree
        // the two differ, and the reference uses the straddle (design §4.3 pass 2). This is
        // also why Top/Bottom are tracked explicitly — Y is not the band's midpoint.
        node.Y = (first.Y + last.Y) / 2;
        return node;
    }

    private static void StackChildren(IReadOnlyList<PackedNode> children, LayoutMetrics metrics)
    {
        var cursor = 0.0;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (i > 0)
            {
                // The wider separation marks where one sibling group ends and the next begins.
                // Two adjacent leaves are simply one leaf pitch apart.
                var needsGroupGap = !children[i - 1].IsLeaf || !child.IsLeaf;
                if (needsGroupGap) cursor += metrics.SiblingGroupGap;
            }

            child.Shift(cursor - child.Top);
            cursor = child.Bottom;
        }
    }
}
