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
/// tick already fully contains the glyph run (0pt excursion measured across the fixture). Task
/// 12 replaced the fixed-offset inflation this paragraph originally described with a search for
/// the nearest matching glyph row instead (<see cref="TryInflateTick"/>) -- Skia's own ticks
/// measure a *different*, smaller offset with the glyph on the *opposite* side (see that
/// method's doc for why one constant can no longer serve both emitters), but the 8.1514-8.1522pt
/// figure above still is the evidence <see cref="GlyphSearchWindow"/> is sized against, and nothing
/// here needed to change for XMind: the nearest row to any of its 344 ticks is still,
/// unambiguously, its own name. The 5 rounded-rect boxes already have real height and need no
/// inflation; their own glyphs (the two largest-font ancestor labels) land inside them directly.
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
    // Safety margin added around a tick's own geometry and its matched glyph row when inflating
    // either into a box. See TryInflateTick for how the row is found.
    private const double Margin = 0.5;

    // How far (in either Y direction) and how wide (in X, past the tick's own extent) to search
    // for the glyph row that names a tick. XMind's measured offset is 8.1514-8.1522pt with the
    // glyph *above* its tick (see GeometryTests.Tick_baseline_offset_is_tight_across_the_fixture,
    // which pins that number independently of this class); Skia's own round-trip fixture measures
    // roughly 4.5-6pt with the glyph *below* its tick (Skia flips the drawing's Y axis internally
    // via a "1 0 0 -1" cm before painting, which inverts which side of the tick's Y the text
    // lands on relative to XMind -- confirmed by the round-trip test's reconstructed names only
    // resolving once the search direction stopped being hardcoded upward). GlyphSearchWindow is
    // set well above both measured offsets but under Skia's own tightest row gap (15pt,
    // LayoutMetrics.LeafPitch) so a search never crosses into a neighbouring row. GlyphSearchXSlack
    // covers padding between a label and its tick/box edge in either emitter.
    private const double GlyphSearchWindow = 12.0;
    private const double GlyphSearchXSlack = 5.0;

    // Once the nearest glyph to a tick is found, other glyphs within this much Y of it join the
    // same matched row (a real row's glyphs sit within ~0.001pt of each other in the reference
    // fixture; this is a generous multiple of that, not a tuned fit).
    private const double RowGroupingTolerance = 1.0;

    // A small tolerance for "is this point inside this box" checks below, absorbing floating-
    // point drift between a box's own (independently rounded) bounding coordinates and a raw
    // path or glyph coordinate that should coincide with it.
    private const double BoxContainsTolerance = 0.1;

    // A closed curvy path must span at least this far in both dimensions to be considered for
    // rounded-rect classification. Excludes every decorative junction-dot family in both
    // emitters (XMind's "cccc end=h" is ~0.01pt; Skia's own dot family measured 1.8-4.6pt) while
    // comfortably admitting the smallest real rounded box in either fixture (all >= 28pt) and,
    // just as importantly, still admitting Skia's ribbon-connector wedge (28 x 10-19.8pt) into
    // the *candidate* pool -- it is rejected by corner coverage below, not by size, because a
    // 4-point wedge cannot occupy 4 independent corners.
    private const double MinRoundedShapeDimension = 10.0;

    // A rounded-rect outline needs at least this many recorded points to trace four rounded
    // corners (each corner contributes several curve points). XMind's real rounded boxes have 9
    // points ("lclclclc"); Skia's have 21 ("lcccclcccclcccclcccc"). Every connector family in
    // both fixtures that also uses curves (XMind's 338 "llcl" elbows have 5 points, its 4
    // "lclcl" brackets have 6; Skia's own elbow family measured 12) falls under this floor.
    private const double MinRoundedShapePoints = 8;

    // A path counts as "flat" (tick or connector-stub candidate, see IsFlatCandidate) only if
    // it is horizontal to within this tolerance -- both emitters' real ticks measure exactly
    // 0pt of height -- and spans at least this much width, which excludes XMind's ~0.01pt
    // decorative dot family (drawn as degenerate "l" paths) while admitting every real tick in
    // either fixture (tens of points wide at minimum).
    private const double FlatnessTolerance = 0.5;
    private const double MinFlatWidth = 2.0;

    // A closed shape smaller than this in both dimensions is a decorative marker (XMind's
    // "cccc"/"l" junction-dot families, ~0.01pt; Skia's own dot family, 1.8-4.6pt measured),
    // never a real connector. Set comfortably below Skia's smallest real connector shape (the
    // 28pt-wide ribbon wedge) and above its largest measured dot (4.6pt).
    private const double DecorativeSizeThreshold = 6.0;

    /// <summary>
    /// Two emitters, one shape vocabulary.
    ///
    /// <para>
    /// XMind's exact (Ops, Terminator) signatures are documented in the class doc above and
    /// were the only ones this method recognised until Task 12 pointed the same engine at our
    /// own Skia-rendered export (design §7.2, the flagship round-trip test). Skia builds paths
    /// differently -- ticks are plain 2-point "l end=S" segments (not XMind's 3-point "ll
    /// end=h" out-and-back), rounded boxes trace each corner as "cccc" instead of a single "c"
    /// (21 points, not 9), and elbow connectors curve through two corners ("lcccclccccl") rather
    /// than one ("llcl"). Matching literal op strings therefore classified zero shapes from a
    /// Skia PDF. This method instead classifies by the geometric property each shape family is
    /// *for*, which both emitters satisfy even though their literal path operators differ:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>A <b>rounded rect</b> is a closed, curved path whose points touch all four corners
    /// of its own bounding box (<see cref="IsRoundedRect"/>) -- true of XMind's 9-point outline
    /// and Skia's 21-point one, but not of any connector shape in either fixture (an elbow only
    /// reaches two of its own bbox's four corners) nor of a decorative dot (round, so it touches
    /// each *edge midpoint*, never a *corner*) nor of Skia's own 4-point ribbon wedge (three
    /// distinct points cannot occupy four independent corners).</item>
    /// <item>A <b>tick</b> is a flat (near-zero-height), sufficiently wide path
    /// (<see cref="IsFlatCandidate"/>) that has a genuine, not-already-boxed name next to it
    /// (<see cref="TryInflateTick"/>). Shape alone cannot finish this job: Skia draws two other
    /// flat, curve-free line families indistinguishable from a tick by geometry --
    /// a "spine" spanning the exact width of each rounded box (an anchor artifact for that box's
    /// own outgoing connectors, confirmed by measuring the spine's width against its box's width
    /// in the round-trip fixture: they match to the point) and short bridging segments between a
    /// box's spine and the next node down (confirmed the same way: a bridging segment's endpoint
    /// coincides exactly with where a real tick begins). Both are told apart from a real tick
    /// only by asking what glyphs actually sit near them: a spine or bridge always resolves to
    /// glyphs already inside a rounded box (the box it spans or leads to); a real tick's glyphs
    /// never do, because tick-shaped nodes never get a rounded box. This clause changes nothing
    /// for XMind, whose ticks and rounded boxes were already geometrically disjoint by
    /// construction, so every one of its 344 real ticks still resolves to its own not-yet-boxed
    /// name.</item>
    /// <item>A <b>connector</b> is anything left over with at least two geometrically distinct
    /// points, excluding decorative shapes smaller than <see cref="DecorativeSizeThreshold"/> in
    /// both dimensions. This deliberately still includes Skia's spine and bridge lines (rejected
    /// above as ticks): a spine's two endpoints sit inside the one box it spans, so
    /// <see cref="Reconstruct.Build"/>'s existing same-box (<c>A == B</c>) filter drops it for
    /// free, exactly as it already drops any degenerate connector; a bridge genuinely carries an
    /// edge's direction (from a box's spine to the next tick down) and is needed for the tree to
    /// reconstruct at all.</item>
    /// </list>
    ///
    /// <para>
    /// <b>The reference fixture's counts must not move.</b> <c>familytree.pdf</c> still yields
    /// exactly 349 boxes (344 ticks + 5 deduped rounded rects) and 348 connectors -- verified by
    /// walking through the documented signature census above: every one of XMind's 9 signature
    /// families lands in the same bucket it did under the old literal-string match (see the
    /// per-family reasoning in this method's private predicates). If a future change to either
    /// emitter's output shifts those counts, narrow the predicates below until both fixtures
    /// pass again; the reference fixture is the regression guard, not a target to relax.
    /// </para>
    /// </summary>
    public static Classified Classify(PageContent page)
    {
        var roundedRectPaths = page.Paths.Where(IsRoundedRect).ToList();

        // The same rectangles are drawn twice (once filled, once stroked) at identical
        // dimensions in both emitters; dedupe by rounded bounding box before emitting boxes, or
        // the box count doubles and Boxes_do_not_overlap becomes unsatisfiable.
        var roundedBoxes = roundedRectPaths
            .Select(p => BoundingBox(p.Points))
            .Select(b => new Box(Math.Round(b.X0, 2), Math.Round(b.Y0, 2), Math.Round(b.X1, 2), Math.Round(b.Y1, 2)))
            .Distinct()
            .ToList();

        var flatCandidates = page.Paths.Where(IsFlatCandidate).ToList();
        var tickPaths = new List<PdfPath>();
        var tickBoxes = new List<Box>();

        foreach (var candidate in flatCandidates)
        {
            var box = TryInflateTick(candidate, roundedBoxes, page.Glyphs);
            if (box is { } realTickBox)
            {
                tickPaths.Add(candidate);
                tickBoxes.Add(realTickBox);
            }
        }

        var boxes = tickBoxes.Concat(roundedBoxes).ToList();

        var roundedRectSet = new HashSet<PdfPath>(roundedRectPaths);
        var tickSet = new HashSet<PdfPath>(tickPaths);

        var connectors = page.Paths
            .Where(p => !roundedRectSet.Contains(p) && !tickSet.Contains(p))
            .Where(HasTwoDistinctPoints)
            .Where(p => !IsDecorative(p))
            .Select(ToConnector)
            .ToList();

        return new Classified(boxes, connectors);
    }

    /// <summary>See the "rounded rect" bullet on <see cref="Classify"/> for why corner coverage,
    /// not a literal op string, is the test.</summary>
    private static bool IsRoundedRect(PdfPath p)
    {
        if (p.Points.Count < MinRoundedShapePoints || !p.Ops.Contains('c')) return false;

        var b = BoundingBox(p.Points);
        var width = b.X1 - b.X0;
        var height = b.Y1 - b.Y0;
        if (width < MinRoundedShapeDimension || height < MinRoundedShapeDimension) return false;

        var tolerance = Math.Max(2.0, Math.Min(width, height) * 0.25);
        return TouchesCorner(p.Points, b.X0, b.Y0, tolerance) &&
               TouchesCorner(p.Points, b.X0, b.Y1, tolerance) &&
               TouchesCorner(p.Points, b.X1, b.Y0, tolerance) &&
               TouchesCorner(p.Points, b.X1, b.Y1, tolerance);
    }

    private static bool TouchesCorner(IReadOnlyList<(double X, double Y)> points, double cx, double cy, double tolerance) =>
        points.Any(p => Math.Abs(p.X - cx) <= tolerance && Math.Abs(p.Y - cy) <= tolerance);

    /// <summary>See the "tick" bullet on <see cref="Classify"/>: flat and wide enough to be a
    /// tick, a box-spanning spine, or a bridge -- <see cref="TryInflateTick"/> tells them
    /// apart.</summary>
    private static bool IsFlatCandidate(PdfPath p)
    {
        // Curved paths are excluded even when their bounding box happens to be flat: some of
        // the fixture's elbow connectors join two nodes at the same Y (no vertical drop needed)
        // and would otherwise be indistinguishable from a tick by bounding box alone (measured:
        // 61 of the reference's 338 "llcl" elbows are exactly zero-height this way). A real tick
        // in both emitters is drawn with straight segments only.
        if (p.Ops.Contains('c')) return false;
        if (p.Points.Count < 2) return false;
        var b = BoundingBox(p.Points);
        return (b.Y1 - b.Y0) <= FlatnessTolerance && (b.X1 - b.X0) > MinFlatWidth;
    }

    /// <summary>
    /// Looks for a glyph row that names <paramref name="candidate"/> and, if one exists and
    /// isn't already claimed by a rounded box, returns the box a real tick inflates to (the
    /// union of the tick's own geometry and its matched glyphs, plus <see cref="Margin"/>).
    /// Returns null for a spine or bridge line (see <see cref="Classify"/>): either it has no
    /// nearby glyphs at all, or the glyphs it finds already sit inside a rounded box.
    ///
    /// <para>
    /// Searching for the matching row (rather than assuming a fixed direction and offset, as the
    /// single-emitter version of this method used to) is what makes this work for both emitters:
    /// XMind's glyphs sit ~8.15pt *above* their tick's Y; Skia's sit ~4.5-6pt *below* (see
    /// <see cref="GlyphSearchWindow"/>). Using the matched glyphs' own bounding box, rather than a
    /// fixed offset, also means this method needs no separate constant per emitter.
    /// </para>
    /// </summary>
    private static Box? TryInflateTick(PdfPath candidate, IReadOnlyList<Box> roundedBoxes, IReadOnlyList<Glyph> glyphs)
    {
        var tick = BoundingBox(candidate.Points);

        // Pick the *nearest* row in X range, not merely any row within the search window: two
        // sibling ticks packed at Skia's minimum row spacing (15pt, LayoutMetrics.LeafPitch) can
        // both fall inside a single window wide enough for XMind's 8.15pt offset, so "any match"
        // would blur two names into one box. Nearest-in-Y is scale-independent and correct
        // because a label always sits closer to its own tick than to a sibling's.
        var inX = glyphs.Where(g => g.X >= tick.X0 - GlyphSearchXSlack && g.X <= tick.X1 + GlyphSearchXSlack).ToList();
        if (inX.Count == 0) return null;

        var nearest = inX.MinBy(g => Math.Abs(g.Y - tick.Y0));
        if (Math.Abs(nearest.Y - tick.Y0) > GlyphSearchWindow) return null;

        var matched = inX.Where(g => Math.Abs(g.Y - nearest.Y) <= RowGroupingTolerance).ToList();
        if (matched.All(g => roundedBoxes.Any(box => IsInside(box, g.X, g.Y)))) return null;

        var glyphBox = BoundingBox(matched.Select(g => (g.X, g.Y)).ToList());
        return new Box(
            Math.Min(tick.X0, glyphBox.X0) - Margin,
            Math.Min(tick.Y0, glyphBox.Y0) - Margin,
            Math.Max(tick.X1, glyphBox.X1) + Margin,
            Math.Max(tick.Y1, glyphBox.Y1) + Margin);
    }

    private static bool IsInside(Box box, double x, double y) =>
        x >= box.X0 - BoxContainsTolerance && x <= box.X1 + BoxContainsTolerance &&
        y >= box.Y0 - BoxContainsTolerance && y <= box.Y1 + BoxContainsTolerance;

    private static bool HasTwoDistinctPoints(PdfPath p)
    {
        if (p.Points.Count < 2) return false;
        var b = BoundingBox(p.Points);
        return (b.X1 - b.X0) > 0 || (b.Y1 - b.Y0) > 0;
    }

    /// <summary>See <see cref="DecorativeSizeThreshold"/>: excludes junction-dot markers from
    /// both emitters so they cannot masquerade as zero-length connectors.</summary>
    private static bool IsDecorative(PdfPath p)
    {
        var b = BoundingBox(p.Points);
        return (b.X1 - b.X0) < DecorativeSizeThreshold && (b.Y1 - b.Y0) < DecorativeSizeThreshold;
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
    ///
    /// <para>
    /// <b>A second closed shape, a second rule.</b> Skia's ribbon connectors (design's "tapered
    /// ribbons to the top level") are closed triangular wedges -- 3 distinct points: two close
    /// together at the wide end (against the parent), one alone at the tapered tip (against the
    /// child). Farthest-pair distance does not reliably separate "the tip" from "one of the base
    /// points" here: measured in the round-trip fixture, one ribbon's farthest pair was the two
    /// *base* points and a base-to-tip pair (base-first, tip-second, matching every other edge's
    /// orientation), while a second, differently-proportioned ribbon's farthest pair was tip-to-
    /// one-base-point but in *tip-first* order -- an inconsistency that broke
    /// <see cref="Reconstruct.Build"/>'s single global start-is-parent/end-is-parent choice and
    /// reconstructed the wrong root. A wedge's tip is always the point that is *not* part of its
    /// own closest pair, regardless of the triangle's proportions, so for this specific 3-point
    /// closed shape the tip is found directly instead: it is consistently the child end.
    /// </para>
    /// </summary>
    private static Connector ToConnector(PdfPath p)
    {
        var distinct = p.Points.Distinct().ToList();
        if (distinct.Count == 3 && IsClosed(p.Points))
        {
            var tipIndex = FarthestFromClosestPair(distinct);
            var baseIndex = (tipIndex + 1) % 3;
            return new Connector(distinct[baseIndex], distinct[tipIndex]);
        }

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

    private static bool IsClosed(IReadOnlyList<(double X, double Y)> points) =>
        points.Count > 1 && points[0] == points[^1];

    /// <summary>Of 3 points, returns the index of whichever is not part of the closest pair --
    /// the wedge's tip (see <see cref="ToConnector"/>).</summary>
    private static int FarthestFromClosestPair(IReadOnlyList<(double X, double Y)> distinct)
    {
        var closestPairSum = -1;
        var closestDistanceSquared = double.MaxValue;

        for (var i = 0; i < 3; i++)
        {
            var j = (i + 1) % 3;
            var dx = distinct[i].X - distinct[j].X;
            var dy = distinct[i].Y - distinct[j].Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closestPairSum = i + j;
            }
        }

        return 3 - closestPairSum;
    }

    // A rounded box's stored coordinates are rounded to 2 decimal places (see Classify, for
    // dedup); a glyph's raw coordinate comes from a separate computation path (text shaping, not
    // path stroking) and is not rounded at all. Measured in the round-trip fixture: a boundary
    // glyph can sit up to ~0.005pt outside its own box's rounded edge (glyph X=170.94524, box
    // X0=170.95) purely from that rounding, which would otherwise silently drop the character.
    // XMind's fixture has 0pt measured excursion (comfortable label padding), so this tolerance
    // is a no-op there; it only rescues Skia's tighter geometry.
    private const double ContainsTolerance = 0.01;

    public static bool Contains(Box b, double x, double y) =>
        x >= b.X0 - ContainsTolerance && x <= b.X1 + ContainsTolerance &&
        y >= b.Y0 - ContainsTolerance && y <= b.Y1 + ContainsTolerance;

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
