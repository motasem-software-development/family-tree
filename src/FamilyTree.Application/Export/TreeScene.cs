namespace FamilyTree.Application.Export;

public readonly record struct ScenePoint(double X, double Y);

/// <summary>
/// Only the centre and the top-level children are drawn as rounded rectangles; every other
/// node is a label sitting on a horizontal tick (design §2.2).
/// </summary>
public enum NodeShape { Tick, RoundedBox }

/// <summary>
/// Centre-to-level-1 links are filled tapered ribbons; everything deeper is a stroked
/// orthogonal elbow (design §4.3).
/// </summary>
public enum ConnectorKind { Ribbon, Elbow }

/// <param name="X">Left edge of the node's box in scene coordinates.</param>
/// <param name="Y">Vertical centre of the node, not its top — every layout pass reasons
/// about centres, and only the renderer converts to a baseline.</param>
public sealed record SceneNode(
    Guid Id,
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    double FontSize,
    string Color,
    NodeShape Shape);

/// <param name="Points">
/// For <see cref="ConnectorKind.Elbow"/>: an orthogonal polyline the renderer rounds at each
/// interior vertex. For <see cref="ConnectorKind.Ribbon"/>: exactly eight points forming a
/// closed teardrop — start edge, two controls, tip, tip, two controls, opposite start edge.
/// </param>
public sealed record SceneConnector(
    ConnectorKind Kind,
    IReadOnlyList<ScenePoint> Points,
    string Color,
    double StrokeWidth);

public sealed record SceneBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

/// <param name="Scale">
/// 1.0 unless overflow forced a uniform reduction (design §4.4). The renderer applies it; the
/// layout coordinates stay unscaled so tests read the same numbers either way.
/// </param>
public sealed record TreeScene(
    IReadOnlyList<SceneNode> Nodes,
    IReadOnlyList<SceneConnector> Connectors,
    SceneBounds Bounds,
    double Scale = 1.0);
