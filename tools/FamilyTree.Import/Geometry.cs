namespace FamilyTree.Import;

public readonly record struct Box(double X0, double Y0, double X1, double Y1);

public sealed record Connector((double X, double Y) A, (double X, double Y) B);

public sealed record Classified(IReadOnlyList<Box> Boxes, IReadOnlyList<Connector> Connectors);

/// <summary>
/// Splits the 1,218 paths extracted from the family-tree PDF into node boxes and the
/// connectors that link them.
///
/// <para>
/// <b>Which signature family is "boxes" -- resolved empirically.</b> The exact
/// (Ops, Terminator) signatures found in the fixture (after fixing <see cref="ContentStream"/>
/// to push path points through the CTM -- see below) are:
///
/// <code>
///   ll        end=h : 344   -- flat "there and back" ticks: 3 collinear points, zero height
///   llcl      end=S : 338   -- elbow connectors (horizontal run, vertical drop, rounded corner)
///   cccc      end=h : 258   -- ~0.01pt closed micro-curves: junction dot markers
///   l         end=f : 129   -- ~0.01pt fill of the same junction dots
///   l         end=S : 129   -- ~0.01pt stroke of the same junction dots
///   lc        end=S :   6   -- short connector stubs (constant width 38.5, variable height)
///   lclclclc  end=f :   5   \ rounded-rect ancestor/root boxes, filled + stroked
///   lclclclc  end=S :   5   /  (same 5 rectangles drawn twice -- dedupe by rounded bbox)
///   lclcl     end=h :   4   -- short brackets linking a rounded box to its tick chain
/// </code>
///
/// This PDF draws a "names on lines" tree, not "names in boxes": most people are labelled by
/// a short horizontal tick (the <c>ll end=h</c> family) with the name floating just above it,
/// and only a handful of special (root/ancestor) nodes get a drawn rounded-rectangle
/// background (the <c>lclclclc</c> family). The arithmetic confirms this split against the
/// two independently-known constants from the characterisation:
///
/// <code>
///   344 ticks + 5 deduped rounded rects = 349  = the expected node count, exactly
///   338 elbows + 6 stubs + 4 brackets   = 348  = 349 - 1 = a tree's edge count, exactly
/// </code>
///
/// The <c>l+ end=h</c> family from the plan's open question is the winner (with the rounded
/// rects folded in per the brief); <c>l+cl end=S</c> (plus its two small stroke cousins) is
/// the connector family. The <c>cccc end=h</c> / <c>l end=f</c> / <c>l end=S</c> families
/// (258 + 129 + 129 = 516 paths) are ~0.01pt closed shapes at the same junction points as the
/// ticks and elbows -- decorative dot markers, drawn three ways (fill, stroke, closed curve).
/// They are neither boxes nor connectors and are discarded.
/// </para>
///
/// <para>
/// <b>Why glyph containment against raw tick geometry scored 0/1887 at first.</b> A tick is a
/// literal line (zero height), so no point can land "inside" it. Measuring every glyph's Y
/// against its x-overlapping tick's Y showed the offset is <b>8.1514-8.1522pt</b> for all 1,862
/// non-ancestor glyphs -- real but tiny variation (under 0.001pt), not floating-point noise; see
/// <c>GeometryTests.Tick_baseline_offset_is_tight_across_the_fixture</c>, which measures this
/// distribution directly and fails loudly if a future fixture change widens it (baseline sits a
/// near-fixed distance above its name-line, independent of font size), and the X range of the
/// tick already fully contains the glyph run (0pt excursion measured across the fixture) -- so a
/// tick is inflated into a box by holding its X range and extending Y by
/// <see cref="TickBaselineOffset"/> (chosen at the low end of the measured spread) plus a small
/// safety margin. The 5 rounded-rect boxes already have real height and need no inflation; their
/// own glyphs (the two largest-font ancestor labels) land inside them directly.
/// </para>
///
/// <para>
/// <b>A prerequisite fix, not part of this task's stated interfaces.</b> <see cref="ContentStream.Read"/>
/// tracked the CTM for glyph placement (Tm x CTM) but never applied it to path construction
/// points (m/l/c recorded raw operands). Many shapes in this PDF are drawn as
/// <c>q / cm (scale, translate) / path ops / Q</c>, reusing shared local template coordinates
/// per instance, so <see cref="PdfPath.Points"/> were in a per-shape local space, not page
/// space, and could not be compared against glyph positions at all. This was fixed at the
/// source (path points now go through the same CTM multiply glyphs already used) since no
/// committed test asserts raw point values and the fix only corrects values flowing through an
/// otherwise-unchanged public shape (<c>PdfPath(Points, Ops, Terminator)</c>).
/// </para>
/// </summary>
public static class Geometry
{
    // Measured across the fixture (GeometryTests.Tick_baseline_offset_is_tight_across_the_fixture):
    // every non-ancestor glyph's Y sits 8.1514-8.1522pt above its tick's Y (baseline offset,
    // near-independent of font size), and 0pt outside the tick's X range. TickBaselineOffset is
    // set at the low end of that measured spread; Margin covers the rest plus floating-point
    // slack. Neither needs to clear the 31.1pt+ vertical spacing between generations at the same
    // X because the measured offset already lands well inside that gap.
    private const double TickBaselineOffset = 8.15;
    private const double Margin = 0.5;

    public static Classified Classify(PageContent page)
    {
        var ticks = page.Paths.Where(p => p.Ops == "ll" && p.Terminator == 'h').ToList();
        var roundedRects = page.Paths.Where(p => p.Ops == "lclclclc").ToList();

        var connectorPaths = page.Paths
            .Where(p =>
                (p.Ops == "llcl" && p.Terminator == 'S') ||
                (p.Ops == "lc" && p.Terminator == 'S') ||
                (p.Ops == "lclcl" && p.Terminator == 'h'))
            .ToList();

        var tickBoxes = ticks.Select(t =>
        {
            var b = BoundingBox(t.Points);
            return new Box(b.X0, b.Y0 - Margin, b.X1, b.Y0 + TickBaselineOffset + Margin);
        });

        // The same 5 rounded rectangles are drawn twice (once filled, once stroked) at
        // identical dimensions; dedupe by rounded bounding box before emitting boxes, or the
        // box count doubles and Boxes_do_not_overlap becomes unsatisfiable.
        var roundedBoxes = roundedRects
            .Select(p => BoundingBox(p.Points))
            .Select(b => new Box(Math.Round(b.X0, 2), Math.Round(b.Y0, 2), Math.Round(b.X1, 2), Math.Round(b.Y1, 2)))
            .Distinct();

        var boxes = tickBoxes.Concat(roundedBoxes).ToList();

        var connectors = connectorPaths
            .Where(p => p.Points.Count > 0)
            .Select(ToConnector)
            .ToList();

        return new Classified(boxes, connectors);
    }

    /// <summary>
    /// Picks the pair of points with the largest pairwise distance as the connector's two
    /// ends, rather than naively using <c>Points[0]</c>/<c>Points[^1]</c>.
    ///
    /// <para>
    /// <b>Why:</b> most connector shapes (the 338 "llcl end=S" elbows, 6 "lc end=S" stubs) are
    /// open paths where the first and last recorded points already are the two extremes, so
    /// this is a no-op for them. But the 4 "lclcl end=h" brackets are <i>closed</i> paths (the
    /// 'h' terminator closes back to the start), so <c>Points[0] == Points[^1]</c> exactly --
    /// both sit at the bracket's near corner (against the box being bracketed), while the
    /// bracket's actual far end (against the other rounded box it links to) sits at the
    /// path's middle points (measured: indices 2 and 3 of 6, each within 0pt of a different
    /// rounded ancestor box). Using the naive first/last pair collapsed all 4 of these
    /// connectors to a zero-length segment sitting entirely inside one box, which <see cref="Reconstruct"/>
    /// then had to discard as carrying no direction -- losing 4 of the tree's 348 edges and
    /// capping the reconstructed hierarchy at 5 roots instead of 1. Farthest-pair selection
    /// recovers the real long diagonal for the closed brackets while leaving the open shapes
    /// unchanged.
    /// </para>
    /// </summary>
    private static Connector ToConnector(PdfPath p)
    {
        var points = p.Points;
        var bestI = 0;
        var bestJ = 0;
        var bestDistanceSquared = -1.0;

        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        return new Connector(points[bestI], points[bestJ]);
    }

    public static bool Contains(Box b, double x, double y) =>
        x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1;

    public static bool Overlaps(Box a, Box b) =>
        a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1;

    private static Box BoundingBox(IReadOnlyList<(double X, double Y)> points)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (x, y) in points)
        {
            if (x < x0) x0 = x;
            if (y < y0) y0 = y;
            if (x > x1) x1 = x;
            if (y > y1) y1 = y;
        }
        return new Box(x0, y0, x1, y1);
    }
}
