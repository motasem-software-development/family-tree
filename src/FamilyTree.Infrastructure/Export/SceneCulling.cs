using FamilyTree.Application.Export;
using SkiaSharp;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Decides whether one scene item can possibly mark a given page (design §4.5, final review
/// Critical 2).
///
/// <para>
/// <b>Why this exists at all, given Skia already clips.</b> Skia's PDF backend does discard
/// drawing that falls outside the page, so the <i>output</i> of an uncullled A4 render is
/// already correct and very nearly byte-identical to a culled one. The cost is entirely
/// UPSTREAM of that clip: <c>SkiaTreeRenderer.DrawShapedRun</c> constructs an
/// <c>SKShaper</c> and runs HarfBuzz over the label before anything is handed to the canvas,
/// and that shaping is serialised process-wide behind <see cref="EmbeddedFonts.ShapingLock"/>.
/// Redrawing every node on every tile therefore cost pages x labels shaping calls -- 1.82M at
/// the 10,000-member cap, measured at 243 s for a single export -- while producing output
/// indistinguishable from the culled render. Which is exactly why the defect survived every
/// output-based test: nothing about the PDF looks wrong, only the clock and the render slot.
/// </para>
///
/// <para>
/// <b>Bounds are deliberately generous.</b> A false positive costs one needless shape call; a
/// false negative silently loses content from a printed poster. Every bound below is inflated
/// past what the item can actually ink.
/// </para>
/// </summary>
public static class SceneCulling
{
    /// <summary>
    /// A node's ink, in scene coordinates, inflated past its box: the label is drawn from the
    /// box's own left edge at a baseline below the centre and may ascend above and descend below
    /// the box, and a tick-shaped node has no box at all -- so the node's font size, not its
    /// <see cref="SceneNode.Height"/>, is what bounds the label vertically.
    /// </summary>
    public static SKRect BoundsOf(SceneNode node)
    {
        var pad = (float)(node.FontSize + CornerAndStrokeSlack);

        return new SKRect(
            (float)node.X - pad,
            (float)(node.Y - node.Height / 2) - pad,
            (float)(node.X + node.Width) + pad,
            (float)(node.Y + node.Height / 2) + pad);
    }

    /// <summary>
    /// A connector's ink, in scene coordinates. Both shapes stay inside the convex hull of
    /// <see cref="SceneConnector.Points"/> -- a cubic Bezier is bounded by its control points,
    /// and rounding an elbow's corner only ever cuts inside the polyline -- so the points'
    /// bounding box, inflated by the full stroke width, contains everything drawn.
    /// </summary>
    public static SKRect BoundsOf(SceneConnector connector)
    {
        var pad = (float)(connector.StrokeWidth + CornerAndStrokeSlack);

        var minX = connector.Points.Min(p => p.X);
        var minY = connector.Points.Min(p => p.Y);
        var maxX = connector.Points.Max(p => p.X);
        var maxY = connector.Points.Max(p => p.Y);

        return new SKRect((float)minX - pad, (float)minY - pad, (float)maxX + pad, (float)maxY + pad);
    }

    /// <summary>
    /// Whether scene-space <paramref name="bounds"/> can mark <paramref name="page"/>.
    ///
    /// <para>
    /// The renderer translates and THEN scales -- <c>Translate(-OffsetX + ContentOffsetX,
    /// -OffsetY)</c> followed by <c>Scale(scale)</c> -- so a scene point lands at
    /// <c>(x*scale - OffsetX + ContentOffsetX, y*scale - OffsetY)</c> in device space, and the
    /// page itself is <c>[0,Width] x [0,Height]</c> there. Offsets and sizes on
    /// <see cref="PageWindow"/> are device-space, which is why the scale multiplies only the
    /// scene coordinates and never the offsets. <see cref="PageWindow.ContentOffsetX"/> must stay
    /// in the transform: a sheet grown wider to fit its caption shifts its whole scene right, and
    /// dropping that term would cull from the wrong window.
    /// </para>
    /// </summary>
    public static bool IsVisible(SKRect bounds, double scale, PageWindow page)
    {
        var s = (float)scale;
        var dx = -page.OffsetX + page.ContentOffsetX;
        var dy = -page.OffsetY;

        var left = bounds.Left * s + dx;
        var right = bounds.Right * s + dx;
        var top = bounds.Top * s + dy;
        var bottom = bounds.Bottom * s + dy;

        return right >= 0 && left <= page.Width && bottom >= 0 && top <= page.Height;
    }

    // Covers the 6pt corner radius, the 1.48pt node stroke drawn centred on its path, and
    // antialiasing spill -- none of which the raw geometry above accounts for.
    private const double CornerAndStrokeSlack = 8.0;
}
