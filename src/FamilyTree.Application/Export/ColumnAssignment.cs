namespace FamilyTree.Application.Export;

/// <summary>
/// Which edge of its column a label is flush with. The names are Arabic, so the eye follows the
/// trailing edge -- which is also the edge every connector leaves from, making one straight
/// vertical line per generation instead of a ragged one.
/// </summary>
public enum ColumnAlignment
{
    /// <summary>Flush with the edge nearest the parent. What the reference PDF does.</summary>
    Leading,

    /// <summary>Flush with the edge nearest the children.</summary>
    Trailing
}

/// <summary>
/// Pass 3 (design §4.3). Within one branch, every node at the same depth shares an x, and each
/// column is sized to its own widest label — which is why the reference's column pitch varies
/// between 50 and 69pt rather than being a fixed indent. Columns are per-branch so a wide name
/// in one branch cannot push a sibling branch outward.
/// </summary>
public static class ColumnAssignment
{
    /// <param name="direction">+1 grows to the right, -1 mirrors to the left.</param>
    /// <param name="alignment">
    /// Defaults to <see cref="ColumnAlignment.Leading"/> so the xmind style keeps reproducing the
    /// reference; the clean style asks for <see cref="ColumnAlignment.Trailing"/>.
    /// </param>
    /// <summary>
    /// The widest label at each depth across a whole set of branches. Passing the result to
    /// <see cref="Assign"/> for every one of those branches makes them share columns, so a
    /// generation lines up across the entire side instead of only within one branch.
    /// </summary>
    public static IReadOnlyDictionary<int, double> WidestByDepth(IEnumerable<PackedNode> branches)
    {
        var widest = new Dictionary<int, double>();
        foreach (var branch in branches)
            foreach (var node in branch.Descend())
                widest[node.Depth] = Math.Max(widest.GetValueOrDefault(node.Depth), node.Width);

        return widest;
    }

    /// <param name="sharedColumns">
    /// Column widths to use instead of measuring this branch alone — see
    /// <see cref="WidestByDepth"/>. Null measures only this branch, which lets a wide name in one
    /// branch avoid pushing its siblings outward, at the cost of branches not lining up.
    /// </param>
    /// <returns>The branch's outer extent, as a signed x in scene coordinates.</returns>
    public static double Assign(
        PackedNode branchRoot, double startX, int direction, LayoutMetrics metrics,
        ColumnAlignment alignment = ColumnAlignment.Leading,
        IReadOnlyDictionary<int, double>? sharedColumns = null)
    {
        var widestByDepth = sharedColumns is null
            ? WidestByDepth([branchRoot])
            : sharedColumns;

        var leadingEdgeByDepth = new Dictionary<int, double>();
        var cursor = startX;
        for (var depth = branchRoot.Depth; widestByDepth.ContainsKey(depth); depth++)
        {
            leadingEdgeByDepth[depth] = cursor;
            cursor += direction * (widestByDepth[depth] + metrics.ColumnGap);
        }

        var extent = startX;
        foreach (var node in branchRoot.Descend())
        {
            var leadingEdge = leadingEdgeByDepth[node.Depth];

            // Trailing alignment pushes each label across its column's own width so the far edges
            // coincide; the slack is the difference between this label and the column's widest.
            var slack = alignment == ColumnAlignment.Trailing
                ? widestByDepth[node.Depth] - node.Width
                : 0;

            // X always means the left edge. Growing leftwards, the leading edge is the node's
            // right edge, so shift by its own width to keep X meaning one thing everywhere.
            node.X = direction > 0
                ? leadingEdge + slack
                : leadingEdge - node.Width - slack;

            var outer = direction > 0 ? node.X + node.Width : node.X;
            extent = direction > 0 ? Math.Max(extent, outer) : Math.Min(extent, outer);
        }

        return extent;
    }
}
