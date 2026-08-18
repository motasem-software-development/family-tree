namespace FamilyTree.Application.Export;

/// <summary>
/// Builds the reference's two connector vocabularies (design §4.3). The renderer draws what it
/// is given and makes no geometric decisions of its own.
/// </summary>
public static class ConnectorBuilder
{
    /// <summary>
    /// Centre → level 1: a closed teardrop, thick at the centre and tapering to the child. The
    /// reference achieves its taper by filling a shape rather than stroking a line, so this is
    /// a fill path — hence a zero stroke width.
    /// </summary>
    public static SceneConnector Ribbon(
        ScenePoint from, ScenePoint to, double halfWidth, string color)
    {
        var midX = (from.X + to.X) / 2;

        var upper = new ScenePoint(from.X, from.Y - halfWidth);
        var lower = new ScenePoint(from.X, from.Y + halfWidth);

        return new SceneConnector(
            ConnectorKind.Ribbon,
            [
                upper,
                new ScenePoint(midX, upper.Y),
                new ScenePoint(midX, to.Y),
                to,
                to,
                new ScenePoint(midX, to.Y),
                new ScenePoint(midX, lower.Y),
                lower
            ],
            color,
            StrokeWidth: 0);
    }

    /// <summary>
    /// Level 2+: parent tick outer end → shared junction column → child row → child tick start.
    /// An orthogonal polyline; the renderer rounds each interior vertex by the corner radius.
    /// </summary>
    public static SceneConnector Elbow(
        ScenePoint from, ScenePoint to, double junctionX, string color, double stroke) =>
        new(
            ConnectorKind.Elbow,
            [
                from,
                new ScenePoint(junctionX, from.Y),
                new ScenePoint(junctionX, to.Y),
                to
            ],
            color,
            stroke);

    /// <summary>The short horizontal rule a label sits on — the connector's final run.</summary>
    public static SceneConnector Tick(ScenePoint from, ScenePoint to, string color, double stroke) =>
        new(ConnectorKind.Elbow, [from, to], color, stroke);
}
