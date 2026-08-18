using FamilyTree.Contracts.FamilyTrees;

namespace FamilyTree.Application.Export;

/// <summary>
/// Build-time scratch node. Mutable by design — the passes fill coordinates in stages — but it
/// never escapes the layout strategy, which freezes it into an immutable
/// <see cref="TreeScene"/>. Nothing outside the Export namespace may depend on it.
/// </summary>
public sealed class PackedNode(
    FamilyTreeNodeResponse source, int depth, int branchIndex, IReadOnlyList<PackedNode> children)
{
    public FamilyTreeNodeResponse Source { get; } = source;
    public int Depth { get; } = depth;

    /// <summary>Index of the top-level ancestor this node hangs from; drives hue (design §3.1).</summary>
    public int BranchIndex { get; } = branchIndex;

    public IReadOnlyList<PackedNode> Children { get; } = children;

    /// <summary>Vertical centre. Set by pass 2, translated by passes 4 and 5.</summary>
    public double Y { get; set; }

    /// <summary>Left edge. Set by pass 3, translated by pass 5.</summary>
    public double X { get; set; }

    public double Width { get; set; }

    /// <summary>Top of the vertical band this node's whole subtree occupies.</summary>
    public double Top { get; set; }

    /// <summary>Bottom of that band. Kept explicitly rather than derived from Y, because a
    /// parent's centre is a straddle and so does not sit at the band's midpoint.</summary>
    public double Bottom { get; set; }

    public double Height => Bottom - Top;

    public bool IsLeaf => Children.Count == 0;

    /// <summary>Moves this node and its whole subtree down by <paramref name="delta"/>.</summary>
    public void Shift(double delta)
    {
        foreach (var node in Descend())
        {
            node.Y += delta;
            node.Top += delta;
            node.Bottom += delta;
        }
    }

    /// <summary>This node and every descendant, pre-order.</summary>
    public IEnumerable<PackedNode> Descend()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.Descend())
                yield return node;
    }
}
