namespace FamilyTree.Application.Export;

/// <summary>
/// Pass 5's translation, shared by every layout strategy: shift the whole scene so it starts at
/// the margin and report its extent. Bounds are always origin-based, so page sizing is simply
/// the bounds' width and height.
/// </summary>
public static class SceneNormaliser
{
    public static TreeScene Normalise(
        IReadOnlyList<SceneNode> nodes,
        IReadOnlyList<SceneConnector> connectors,
        LayoutMetrics metrics)
    {
        if (nodes.Count == 0) return new TreeScene([], [], new SceneBounds(0, 0, 0, 0));

        var xs = nodes.SelectMany(n => new[] { n.X, n.X + n.Width })
            .Concat(connectors.SelectMany(c => c.Points.Select(p => p.X)))
            .ToList();
        var ys = nodes.SelectMany(n => new[] { n.Y - n.Height / 2, n.Y + n.Height / 2 })
            .Concat(connectors.SelectMany(c => c.Points.Select(p => p.Y)))
            .ToList();

        var dx = metrics.Margin - xs.Min();
        var dy = metrics.Margin - ys.Min();

        return new TreeScene(
            nodes.Select(n => n with { X = n.X + dx, Y = n.Y + dy }).ToList(),
            connectors.Select(c => c with
            {
                Points = c.Points.Select(p => new ScenePoint(p.X + dx, p.Y + dy)).ToList()
            }).ToList(),
            new SceneBounds(
                0, 0,
                xs.Max() + dx + metrics.Margin,
                ys.Max() + dy + metrics.Margin));
    }
}
