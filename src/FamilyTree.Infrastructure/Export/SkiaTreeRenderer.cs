using FamilyTree.Domain.Common;
using System.Globalization;
using System.Text;
using FamilyTree.Application.Export;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FamilyTree.Infrastructure.Export;

public enum ExportPageFormat { Sheet, A4 }

public interface ITreeRenderer
{
    /// <param name="ct">
    /// Observed once per page. A large A4 export is minutes of CPU inside one of only two
    /// process-wide render slots, so a client that has already disconnected must be able to stop
    /// it (final review, Critical 2) -- a plain BCL type, so this adds no dependency to any
    /// caller.
    /// </param>
    byte[] Render(
        TreeScene scene,
        ExportPageFormat format,
        PdfCaption? caption = null,
        CancellationToken ct = default);
}

/// <summary>
/// Draws a <see cref="TreeScene"/> into a PDF (design §4.1). Makes no layout decisions: every
/// coordinate arrives already computed, which is what keeps the geometry unit-testable without
/// a font or a native binary.
/// </summary>
public sealed class SkiaTreeRenderer : ITreeRenderer
{
    private const float CornerRadius = 6f;
    private const float CaptionFontSize = 8f;
    private const float CaptionMinFontSize = 6f;
    private const float CaptionBottomPadding = 10f;
    private const float CaptionFitMargin = 2f;

    // Margin around the caption on a sheet page grown to fit it (design §4.6, Important 2 fix)
    // -- matches the design's own scene margin (LayoutOptions.LayoutMetrics.Margin) for visual
    // consistency, duplicated here rather than threaded through because SkiaTreeRenderer.Render
    // only receives the already-built TreeScene, not the LayoutOptions that produced it.
    private const float CaptionPageMargin = 24f;

    // A generous line-length ceiling (design §4.6, Important 2 fix): below this, a sheet page
    // simply grows to fit the caption at full size, no shrink or truncation needed. Above it
    // (an enormous family tree name), growing the page further stops being the right trade-off
    // and the existing shrink-then-ellipsise behaviour takes over, bounded to this width.
    private const float CaptionMaxWidth = 600f;

    // Reserved below the scene (sheet) or clipped from the bottom of every tile (A4) so the
    // caption always has genuinely empty space to sit in, regardless of scale (design §4.6,
    // Important 4). Comfortably covers an 8pt line at 10pt bottom padding with headroom above.
    internal const float CaptionBandHeight = 28f;

    // The palette's own centre grey (design LayoutOptions.BranchPalette.CentreColor):
    // restrained, not attention-seeking -- this is furniture, not data (design §4.6).
    private static readonly SKColor CaptionColor = SKColor.Parse("#8793A5");

    /// <param name="caption">
    /// Null draws no caption. When present it is drawn in a band reserved below the tree's own
    /// content, in device points, outside the scene's own Translate/Scale -- it must not shrink
    /// or grow with the tree, and must not collide with it at any scale (design §4.6). Both
    /// sheet and A4 caption every page: sheet has exactly one, and every A4 tile reserves its
    /// own band. On a sheet whose own natural width is narrower than the caption needs, the page
    /// grows to fit the caption instead of clipping it (Important 2 fix) -- the tree stays
    /// centred in the wider page.
    /// </param>
    public byte[] Render(
        TreeScene scene,
        ExportPageFormat format,
        PdfCaption? caption = null,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream();

        using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Creator = "Family Tree",
            Title = "Family Tree"
        }))
        {
            var captionBandHeight = caption is null ? 0f : CaptionBandHeight;

            // The ONE resolution: ResolveLayoutForFormat is the only place either the drawing
            // path or ComputeCaptionRunPositionsForTesting (the test seam) computes a
            // CaptionLayout, so the two cannot drift apart the way a caller-supplied pageWidth
            // guess previously let them (Round-3 review, finding 2). Sheet's own width is not an
            // input to this at all -- see ResolveLayoutForFormat -- it's an OUTPUT: the page
            // grows to fit whatever this resolves to.
            var layout = caption is null ? null : ResolveLayoutForFormat(caption, format);

            var sheetMinWidth = format == ExportPageFormat.Sheet && layout is not null
                ? layout.TotalWidth + 2 * CaptionPageMargin
                : 0f;

            var pages = Paginate(scene, format, captionBandHeight, sheetMinWidth).ToList();

            GuardPageCount(format, pages.Count);

            // Computed ONCE for the whole document, not once per page: bounds depend only on the
            // scene, and recomputing them per page would put an O(pages x items) cost back in
            // exactly the place this culling exists to take one out of.
            var nodes = scene.Nodes.Select(node => (Item: node, Bounds: SceneCulling.BoundsOf(node))).ToList();
            var connectors = scene.Connectors
                .Select(connector => (Item: connector, Bounds: SceneCulling.BoundsOf(connector)))
                .ToList();

            foreach (var page in pages)
            {
                // Per page, not per item: a page is the unit of work worth abandoning, and a
                // single page is bounded by construction.
                ct.ThrowIfCancellationRequested();

                var canvas = document.BeginPage(page.Width, page.Height);
                canvas.Clear(SKColors.White);

                canvas.Save();
                if (format == ExportPageFormat.A4 && caption is not null)
                    canvas.ClipRect(SKRect.Create(page.Width, page.Height - captionBandHeight));

                canvas.Translate(-page.OffsetX + page.ContentOffsetX, -page.OffsetY);
                canvas.Scale((float)scene.Scale);

                // Skia's own clip would discard these anyway -- but only AFTER DrawShapedRun has
                // already shaped the label through HarfBuzz, under a process-wide lock. Skipping
                // them here is what makes A4 cost the scene's size rather than pages x scene.
                foreach (var (connector, bounds) in connectors)
                    if (SceneCulling.IsVisible(bounds, scene.Scale, page)) DrawConnector(canvas, connector);

                foreach (var (node, bounds) in nodes)
                    if (SceneCulling.IsVisible(bounds, scene.Scale, page)) DrawNode(canvas, node);

                canvas.Restore();

                if (layout is not null) DrawCaption(canvas, page, layout);

                document.EndPage();
            }

            document.Close();
        }

        return stream.ToArray();
    }

    private readonly record struct CaptionRun(string Text, float Width);

    private sealed record CaptionSegmentLayout(IReadOnlyList<CaptionRun> Runs, float Width);

    private sealed record CaptionLayout(
        IReadOnlyList<CaptionSegmentLayout> Segments,
        float TotalWidth,
        float FontSize,
        float Gap,
        bool RightToLeft);

    /// <summary>
    /// The one place a <see cref="CaptionLayout"/> is resolved for a given page format --
    /// called by <see cref="Render"/> to draw with and by
    /// <see cref="ComputeCaptionRunPositionsForTesting"/> to verify, so the two cannot diverge
    /// (Round-3 review, finding 2: a caller-supplied pageWidth guess previously let the test
    /// seam certify a layout production never actually drew).
    ///
    /// Sheet: the caption's own width is not an input here at all -- the page is what adapts
    /// (Important 2 fix), so this resolves the caption at its natural size, falling back to
    /// shrink-then-ellipsise only past <see cref="CaptionMaxWidth"/>. A4: every tile shares the
    /// same fixed physical width, so that constant is the fit target.
    /// </summary>
    private static CaptionLayout ResolveLayoutForFormat(PdfCaption caption, ExportPageFormat format)
    {
        if (format == ExportPageFormat.A4) return ResolveCaptionLayout(caption, A4Paginator.PageWidth);

        var natural = ResolveCaptionLayout(caption, float.MaxValue);
        return natural.TotalWidth <= CaptionMaxWidth ? natural : ResolveCaptionLayout(caption, CaptionMaxWidth);
    }

    /// <summary>
    /// Resolves the caption once per render: shrinks the font toward <see cref="CaptionMinFontSize"/>
    /// if it doesn't fit <paramref name="fitWidth"/> at 8pt, then truncates the family tree NAME
    /// segment (never a count or the date) with an ellipsis if it still doesn't fit at the floor
    /// (design §4.6, Important 3). Pass <see cref="float.MaxValue"/> to resolve the caption at
    /// its natural, unconstrained size.
    /// </summary>
    private static CaptionLayout ResolveCaptionLayout(PdfCaption caption, float fitWidth)
    {
        // A small safety margin, not just cosmetic: it keeps the fit decision away from an
        // exact tie, so a sub-point measurement jitter between two otherwise-identical shaping
        // calls can't flip which side of "fits" / "doesn't fit" a borderline caption lands on.
        fitWidth = fitWidth == float.MaxValue ? fitWidth : fitWidth - CaptionFitMargin;

        var segments = CaptionLocalizer.Segments(caption);
        var fontSize = CaptionFontSize;
        var (layout, totalWidth) = BuildSegmentLayouts(segments, fontSize);

        while (totalWidth > fitWidth && fontSize > CaptionMinFontSize)
        {
            fontSize = MathF.Max(CaptionMinFontSize, fontSize - 0.5f);
            (layout, totalWidth) = BuildSegmentLayouts(segments, fontSize);
        }

        if (totalWidth > fitWidth)
        {
            var nameIndex = IndexOfName(segments);
            if (nameIndex >= 0)
            {
                var elements = TextElements(segments[nameIndex].Text);
                var mutable = segments.ToList();

                for (var keep = elements.Count - 1; keep >= 0 && totalWidth > fitWidth; keep--)
                {
                    var truncated = keep > 0
                        ? string.Concat(elements.Take(keep)) + "…"
                        : "…";
                    mutable[nameIndex] = mutable[nameIndex] with { Text = truncated };
                    (layout, totalWidth) = BuildSegmentLayouts(mutable, fontSize);
                }

                segments = mutable;
            }
        }

        var gap = (float)SkiaTextMeasurer.Measure(" ", fontSize);
        return new CaptionLayout(layout, totalWidth, fontSize, gap, caption.Language == CaptionLanguage.Ar);
    }

    /// <summary>
    /// Splits every segment's text into script-homogeneous runs (design §4.6, finding 3: a
    /// segment is a logical field, not a promise of single-script text -- the free-form family
    /// tree name above all needs this) and measures each run on its own. No whitespace survives
    /// into any run's own text (<see cref="EmbeddedFonts.SplitByScript"/> drops it): spacing --
    /// between segments AND between two runs that came from splitting the same segment alike --
    /// is always the same explicit, measured gap, never a character living inside a run's text
    /// (Round-2 review finding 1, and Round-3 review finding 3: the same defect resurfaced one
    /// level down, inside a mixed-script segment, when whitespace was merely reattached to a
    /// neighbouring run instead of stripped out).
    /// </summary>
    private static (List<CaptionSegmentLayout> Layout, float TotalWidth) BuildSegmentLayouts(
        IReadOnlyList<CaptionSegment> segments, float fontSize)
    {
        var gap = (float)SkiaTextMeasurer.Measure(" ", fontSize);

        var layout = segments.Select(segment =>
        {
            var runs = EmbeddedFonts.SplitByScript(segment.Text)
                .Select(text => new CaptionRun(text, (float)SkiaTextMeasurer.Measure(text, fontSize)))
                .ToList();
            var width = runs.Sum(r => r.Width) + gap * MathF.Max(0, runs.Count - 1);
            return new CaptionSegmentLayout(runs, width);
        }).ToList();

        var totalWidth = layout.Sum(s => s.Width) + gap * MathF.Max(0, layout.Count - 1);
        return (layout, totalWidth);
    }

    private static int IndexOfName(IReadOnlyList<CaptionSegment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
            if (segments[i].Kind == CaptionSegmentKind.Name) return i;
        return -1;
    }

    /// <summary>Grapheme clusters, not UTF-16 code units -- truncating by <c>char</c> index can
    /// split a surrogate pair or detach a combining mark from its base (design §4.6, finding
    /// 5).</summary>
    private static List<string> TextElements(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add((string)enumerator.Current);
        return elements;
    }

    /// <summary>
    /// Every run's absolute X position and width, in final drawing order for
    /// <see cref="CaptionLayout.RightToLeft"/> -- right-to-left walks segments from the right
    /// edge backward (so the first logical segment ends up rightmost); left-to-right walks them
    /// from the left edge forward. Each segment's own runs are then walked the SAME way, inside
    /// that segment's own slot (<see cref="LayoutSegmentRuns"/>) -- direction-consistent
    /// ordering, not full Unicode bidi (see that method's remarks for exactly what that means
    /// and does not mean).
    /// </summary>
    private static IEnumerable<(string Text, float X, float Width)> LayoutCaptionRuns(
        CaptionLayout layout, float pageWidth)
    {
        var left = (pageWidth - layout.TotalWidth) / 2f;

        if (layout.RightToLeft)
        {
            var x = left + layout.TotalWidth;
            for (var i = 0; i < layout.Segments.Count; i++)
            {
                x -= layout.Segments[i].Width;
                foreach (var run in LayoutSegmentRuns(layout.Segments[i], x, layout.Gap, rightToLeft: true))
                    yield return run;

                if (i < layout.Segments.Count - 1) x -= layout.Gap;
            }
        }
        else
        {
            var x = left;
            for (var i = 0; i < layout.Segments.Count; i++)
            {
                foreach (var run in LayoutSegmentRuns(layout.Segments[i], x, layout.Gap, rightToLeft: false))
                    yield return run;

                x += layout.Segments[i].Width;
                if (i < layout.Segments.Count - 1) x += layout.Gap;
            }
        }
    }

    /// <summary>
    /// Lays out one segment's own runs within its slot, starting at <paramref name="segmentLeftX"/>.
    /// A segment's runs are ordered the SAME way the caption's segments are: right-to-left for an
    /// Ar caption (the first-typed run ends up rightmost within the segment), left-to-right for
    /// En. This is a deliberate, deliberately limited rule -- direction-consistent ordering, not
    /// an implementation of the Unicode Bidirectional Algorithm (UAX #9). A caption is four short
    /// fields, not a paragraph of arbitrary bidi text, and does not warrant a bidi engine; but a
    /// mixed-script family tree name (Latin and Arabic words together) may therefore lay out
    /// differently here than a full bidi implementation would for the same text -- this rule only
    /// promises that a name's individual words each shape correctly (Critical 1/2) and that the
    /// whole caption's fields read in the caption's own declared direction, not that embedded
    /// runs resolve their relative order the way UAX #9 would from first principles.
    /// </summary>
    private static IEnumerable<(string Text, float X, float Width)> LayoutSegmentRuns(
        CaptionSegmentLayout segment, float segmentLeftX, float gap, bool rightToLeft)
    {
        if (rightToLeft)
        {
            var x = segmentLeftX + segment.Width;
            for (var i = 0; i < segment.Runs.Count; i++)
            {
                x -= segment.Runs[i].Width;
                yield return (segment.Runs[i].Text, x, segment.Runs[i].Width);
                if (i < segment.Runs.Count - 1) x -= gap;
            }
        }
        else
        {
            var x = segmentLeftX;
            for (var i = 0; i < segment.Runs.Count; i++)
            {
                yield return (segment.Runs[i].Text, x, segment.Runs[i].Width);
                x += segment.Runs[i].Width;
                if (i < segment.Runs.Count - 1) x += gap;
            }
        }
    }

    /// <summary>Draws every run independently -- shaped, measured, and font-selected on its
    /// own -- so a run being single-script (which every run is, by construction) is what keeps
    /// HarfBuzz's shaping from reversing it (design §4.6, Critical 1/2 fix).</summary>
    private static void DrawCaption(SKCanvas canvas, PageWindow page, CaptionLayout layout)
    {
        var baseline = page.Height - CaptionBottomPadding;
        foreach (var (text, x, _) in LayoutCaptionRuns(layout, page.Width))
            DrawShapedText(canvas, text, x, baseline, layout.FontSize, CaptionColor);
    }

    /// <summary>
    /// Test-only seam (see AssemblyInfo.cs): the caption's computed run geometry. Calls
    /// <see cref="ResolveLayoutForFormat"/> -- the SAME resolution <see cref="Render"/> calls to
    /// draw with, given the SAME (caption, format) inputs -- rather than accepting a
    /// caller-supplied pageWidth the way an earlier version did, which let this seam certify a
    /// layout production never actually drew (Round-3 review, finding 2: a capped sheet caption
    /// resolved against <see cref="CaptionMaxWidth"/> in production but against whatever pageWidth
    /// a test happened to pass here, giving two different layouts for the same caption). Drift
    /// between "what got drawn" and "what this returns" is now impossible by construction: there
    /// is exactly one call to <see cref="ResolveLayoutForFormat"/> per (caption, format), and both
    /// this method and <see cref="Render"/> make it the same way.
    ///
    /// The page width used to centre these runs is reconstructed identically to how
    /// <see cref="Render"/> would compute it for a caption that is the widest thing on its own
    /// page: A4's fixed physical width, or -- for Sheet -- exactly <see cref="CaptionPageMargin"/>
    /// on each side of the resolved caption (matching <c>SheetPaginator.Pages</c>'s <c>minWidth</c>
    /// term). If the scene itself is wider than the caption, the actual sheet page (and therefore
    /// the actual centring offset) is wider still -- every caller of this seam today uses a scene
    /// small enough that the caption alone dictates the page width, so this matches production
    /// exactly for those cases; it is not a general substitute for reading the real page width
    /// back out of a rendered PDF (which the Important-2 regression tests still do).
    /// </summary>
    internal static IReadOnlyList<(string Text, float X, float Width)> ComputeCaptionRunPositionsForTesting(
        PdfCaption caption, ExportPageFormat format)
    {
        var layout = ResolveLayoutForFormat(caption, format);
        var pageWidth = format == ExportPageFormat.A4
            ? A4Paginator.PageWidth
            : layout.TotalWidth + 2 * CaptionPageMargin;

        return LayoutCaptionRuns(layout, pageWidth).ToList();
    }

    /// <summary>
    /// A hard ceiling on how many A4 tiles one export may emit.
    ///
    /// <para>
    /// Per-page culling is the real cost fix and brings A4 back to roughly sheet cost; this is
    /// the backstop that keeps the FORMAT from being an unbounded lever even if culling ever
    /// regresses, because the sheet path's own 413 message actively directs callers here.
    /// Post-culling measurements through the real adapter (single-threaded Release, three
    /// generations below the root): 911 members / 18 pages 0.89 s, 1,911 / 36 pages 2.01 s,
    /// 9,911 / 188 pages 11.25 s -- against 243.17 s for the same last case before culling.
    /// A tree at the 10,000-member cap therefore lands near 190 pages; 600 is chosen a little
    /// over 3x above that, high enough that no tree a tenant can actually build trips it, low
    /// enough that a pathological aspect ratio still cannot turn one request into unbounded page
    /// emission. That last case is the one the member cap genuinely cannot see: pagination is
    /// driven by the scene's BOUNDS, and a scene can tile into thousands of nearly-empty pages
    /// with very few members in it. A refusal names the cap, so the number is not a mystery to
    /// the caller.
    /// </para>
    /// </summary>
    internal const int MaxA4Pages = 600;

    private static void GuardPageCount(ExportPageFormat format, int pageCount)
    {
        if (format != ExportPageFormat.A4 || pageCount <= MaxA4Pages) return;

        throw new TooLargeException(
            "EXPORT_TREE_TOO_LARGE",
            $"This tree needs {pageCount} A4 pages, above the {MaxA4Pages} page export limit.",
            "a4-page-cap");
    }

    private static IEnumerable<PageWindow> Paginate(
        TreeScene scene, ExportPageFormat format, float captionBandHeight, float sheetMinWidth) =>
        format switch
        {
            ExportPageFormat.Sheet => SheetPaginator.Pages(scene, captionBandHeight, sheetMinWidth),
            ExportPageFormat.A4 => A4Paginator.Pages(scene, captionBandHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static void DrawConnector(SKCanvas canvas, SceneConnector connector)
    {
        var isRibbon = connector.Kind == ConnectorKind.Ribbon;

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(connector.Color),
            IsAntialias = true,
            Style = isRibbon ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = (float)connector.StrokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        using var path = isRibbon ? RibbonPath(connector) : ElbowPath(connector);
        canvas.DrawPath(path, paint);
    }

    /// <summary>Eight points: start edge, two controls, tip — then back along the mirror.</summary>
    private static SKPath RibbonPath(SceneConnector connector)
    {
        var p = connector.Points;
        using var builder = new SKPathBuilder();

        builder.MoveTo(F(p[0]));
        builder.CubicTo(F(p[1]), F(p[2]), F(p[3]));
        builder.LineTo(F(p[4]));
        builder.CubicTo(F(p[5]), F(p[6]), F(p[7]));
        builder.Close();

        return builder.Detach();
    }

    /// <summary>Orthogonal polyline, rounded at each interior vertex.</summary>
    private static SKPath ElbowPath(SceneConnector connector)
    {
        var p = connector.Points;
        using var builder = new SKPathBuilder();

        builder.MoveTo(F(p[0]));
        for (var i = 1; i < p.Count - 1; i++)
            builder.ArcTo(F(p[i]), F(p[i + 1]), CornerRadius);
        builder.LineTo(F(p[^1]));

        return builder.Detach();
    }

    private static void DrawNode(SKCanvas canvas, SceneNode node)
    {
        if (node.Shape == NodeShape.RoundedBox) DrawBox(canvas, node);
        DrawLabel(canvas, node);
    }

    private static void DrawBox(SKCanvas canvas, SceneNode node)
    {
        var rect = new SKRect(
            (float)node.X, (float)(node.Y - node.Height / 2),
            (float)(node.X + node.Width), (float)(node.Y + node.Height / 2));

        using var fill = new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true
        };
        using var stroke = new SKPaint
        {
            Color = SKColor.Parse(node.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.48f,
            IsAntialias = true
        };

        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, fill);
        canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, stroke);
    }

    /// <summary>
    /// Vertical offset from a tick's line to its label baseline, as a fraction of the font size.
    /// Negative lifts the glyphs above the line: the name sits ON the tick rather than being
    /// bisected by it, which is the mind-map convention the design asks for. Arabic descenders
    /// still graze the line, which is what "resting on it" should look like.
    /// </summary>
    private const double TickLabelLift = -0.15;

    /// <summary>
    /// Offset for a label centred inside a rounded box: half the cap height, so the glyphs sit
    /// optically centred on the box's vertical centre.
    /// </summary>
    private const double BoxLabelCentring = 0.35;

    private static void DrawLabel(SKCanvas canvas, SceneNode node)
    {
        var offset = node.Shape == NodeShape.Tick ? TickLabelLift : BoxLabelCentring;
        var baseline = (float)(node.Y + node.FontSize * offset);
        DrawShapedText(
            canvas, node.Label, (float)node.X, baseline, (float)node.FontSize, SKColors.Black);
    }

    /// <summary>
    /// Shapes and paints one line of text -- node label or bottom-margin caption alike -- at a
    /// given origin. Shared so the caption gets the same Arabic-shaping and searchability
    /// handling as every node label, rather than a second, divergent text path.
    ///
    /// <para>
    /// <b>Mixed script.</b> <see cref="EmbeddedFonts.For"/> picks ONE typeface for a whole
    /// string, so a free-form name mixing scripts ("Ali سالم") resolves the minority script
    /// against a typeface with no coverage for it and prints .notdef boxes. Task 16 fixed this
    /// for the caption by splitting segments with <see cref="EmbeddedFonts.SplitByScript"/> and
    /// calling this method once per run -- but node labels, which are the document's entire
    /// payload, still arrived here whole (final review, Critical 1). The split now lives HERE,
    /// so both callers get it from one rule: mixed text is drawn run by run, each run shaped and
    /// font-selected on its own, with the pen advanced by an explicit measured word gap between
    /// runs (whitespace does not survive into any run -- see <see cref="EmbeddedFonts.SplitByScript"/>).
    /// The caption path is unaffected in behaviour: it still splits first, so every string
    /// reaching here from it is already single-script and takes the single-run path below.
    /// </para>
    ///
    /// <para>
    /// Run ORDER follows the first run's own script -- the Unicode "first strong character sets
    /// the base direction" rule (UAX #9 P2/P3), applied at run granularity. As with the caption
    /// (see <see cref="LayoutSegmentRuns"/>), this is direction-consistent ordering, not a bidi
    /// engine: it promises each run shapes correctly and that the label reads in its own leading
    /// script's direction, not that embedded runs resolve exactly as UAX #9 would. Note that
    /// digits and punctuation count as non-Arabic under <see cref="EmbeddedFonts.IsArabicText"/>,
    /// so a name beginning with a digit takes the left-to-right ordering.
    /// </para>
    /// </summary>
    private static void DrawShapedText(
        SKCanvas canvas, string text, float x, float baselineY, float fontSize, SKColor color)
    {
        if (!EmbeddedFonts.IsMixedScript(text))
        {
            DrawShapedRun(canvas, text, x, baselineY, fontSize, color);
            return;
        }

        var runs = EmbeddedFonts.SplitByScript(text);
        var widths = runs.Select(run => (float)SkiaTextMeasurer.Measure(run, fontSize)).ToList();
        var gap = (float)SkiaTextMeasurer.Measure(" ", fontSize);

        // Same total the layout sized this label's column from (SkiaTextMeasurer.Measure takes
        // the identical sum), which is what lets the right-to-left branch start from the right
        // edge of the very box the layout reserved.
        var total = widths.Sum() + gap * MathF.Max(0, runs.Count - 1);

        if (EmbeddedFonts.IsArabicText(runs[0]))
        {
            var pen = x + total;
            for (var i = 0; i < runs.Count; i++)
            {
                pen -= widths[i];
                DrawShapedRun(canvas, runs[i], pen, baselineY, fontSize, color);
                pen -= gap;
            }
        }
        else
        {
            var pen = x;
            for (var i = 0; i < runs.Count; i++)
            {
                DrawShapedRun(canvas, runs[i], pen, baselineY, fontSize, color);
                pen += widths[i] + gap;
            }
        }
    }

    /// <summary>
    /// Shapes and paints ONE script-homogeneous run. Every comment below about /ToUnicode and
    /// mark handling applies per run; <see cref="DrawShapedText"/> is what guarantees a run is
    /// homogeneous before it gets here.
    /// </summary>
    private static void DrawShapedRun(
        SKCanvas canvas, string text, float x, float baselineY, float fontSize, SKColor color)
    {
        var typeface = EmbeddedFonts.For(text);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var font = new SKFont(typeface, fontSize);

        // See EmbeddedFonts.ShapingLock for why this lock exists (concurrent shaping against a
        // shared typeface can corrupt HarfBuzz's/Skia's own caches) and why it is scoped this
        // narrowly rather than covering the whole method (a documented throughput trade-off, not
        // an oversight): SKShaper construction, Shape, and MarkIndices's own SKFont.MeasureText
        // calls are the operations Round-2's determinism flake actually implicated, and are what
        // this lock guarantees are serialised.
        //
        // Everything after this block draws with this call's own already-shaped data on this
        // call's own canvas -- EXCEPT DrawMarksAsPaths's font.GetGlyphPath(glyph), which does
        // walk the shared SKTypeface's glyph-outline cache, the same kind of shared state the
        // lock exists to protect (Round-3 review, finding 4: an earlier version of this comment
        // claimed otherwise). It is left outside the lock deliberately, not by oversight: 48
        // concurrent renders across 4 threads produced identical output with it unlocked, marks
        // are a small fraction of a caption's or a label's glyphs, and extending the lock further
        // would only worsen the throughput trade-off already accepted above for the operations
        // actually shown to need it. This is a narrower guarantee than "provably race-free" --
        // it is "the specific hazard this codebase has actually observed is covered, and this
        // additional path has not (yet) demonstrated the same failure mode under load."
        SKShaper.Result shaped;
        HashSet<int> marks;
        lock (EmbeddedFonts.ShapingLock)
        {
            using var shaper = new SKShaper(typeface);
            shaped = shaper.Shape(text, x, baselineY, font);
            marks = MarkIndices(shaped, font);
        }

        // Shaping is what joins Arabic correctly, but two things stand between a shaped
        // glyph run and a *searchable* one:
        //
        // 1. SKCanvas.DrawShapedText derives its /ToUnicode CMap by reverse-scanning the
        //    font's own cmap table from glyph id back to a codepoint — and Noto Sans
        //    Arabic's cmap also carries legacy presentation-form entries that win that scan,
        //    so pdftotext recovers glyph-shape codepoints (e.g. U+FE8E) or U+0000, not the
        //    letters that were actually typed. A hand-built text blob that attaches the
        //    source UTF-8 text and HarfBuzz's cluster map to the run instead makes Skia's
        //    PDF backend build /ToUnicode from the real text.
        //
        // 2. Some letters (e.g. the dot on ف or ي) shape to a base glyph plus a zero-advance
        //    mark positioned via GPOS. Every attempt to keep that mark in the text layer —
        //    interleaved with its base, reordered by X, or drawn as its own zero-width text
        //    run — leaves either a stray character or a spurious mid-word space in
        //    pdftotext's output, because the mark's presence as a *text* object (however
        //    it's ordered or associated) still perturbs pdftotext's spatial word-gap
        //    reconstruction. Painting marks as plain vector paths instead removes them from
        //    the text layer entirely: invisible to every extractor, and the remaining
        //    base-glyph run is a clean, monotonic sequence that matches the font's own
        //    declared advances.
        //
        // Do not swap this back for DrawShapedText, and do not fold the marks back into the
        // text run, without re-running the searchability gate.
        DrawMarksAsPaths(canvas, shaped, font, paint, marks);

        using var blob = BuildShapedTextBlob(text, shaped, font, marks);
        if (blob is not null) canvas.DrawText(blob, 0, 0, paint);
    }

    /// <summary>Zero-advance glyphs (dots, combining marks) painted as outlines, not text.</summary>
    private static void DrawMarksAsPaths(
        SKCanvas canvas, SKShaper.Result shaped, SKFont font, SKPaint paint, HashSet<int> marks)
    {
        foreach (var i in marks)
        {
            var glyph = (ushort)shaped.Codepoints[i];
            using var path = font.GetGlyphPath(glyph);
            if (path.IsEmpty) continue;

            canvas.Save();
            canvas.Translate(shaped.Points[i].X, shaped.Points[i].Y);
            canvas.DrawPath(path, paint);
            canvas.Restore();
        }
    }

    private static SKTextBlob? BuildShapedTextBlob(
        string text, SKShaper.Result shaped, SKFont font, HashSet<int> marks)
    {
        var baseIndices = Enumerable.Range(0, shaped.Codepoints.Length)
            .Where(i => !marks.Contains(i))
            .ToArray();
        if (baseIndices.Length == 0) return null;

        var utf8Text = Encoding.UTF8.GetBytes(text);
        var glyphs = baseIndices.Select(i => (ushort)shaped.Codepoints[i]).ToArray();
        var positions = baseIndices.Select(i => shaped.Points[i]).ToArray();
        var clusters = baseIndices.Select(i => shaped.Clusters[i]).ToArray();

        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocatePositionedTextRun(font, glyphs.Length, utf8Text.Length);
        run.SetGlyphs(glyphs);
        run.SetPositions(positions);
        run.SetText(utf8Text);
        run.SetClusters(clusters);

        return builder.Build();
    }

    /// <summary>
    /// A glyph is a mark, not a standalone character, when it has zero advance width AND at
    /// least one other glyph shares its cluster — a bare combining mark with no base of its own
    /// (real in Arabic text, though not in this design's node labels) must stay in the
    /// searchable text run, since dropping it there would be the only glyph for that character.
    /// </summary>
    private static HashSet<int> MarkIndices(SKShaper.Result shaped, SKFont font)
    {
        var count = shaped.Codepoints.Length;
        var clusterCounts = new Dictionary<uint, int>();
        for (var i = 0; i < count; i++)
            clusterCounts[shaped.Clusters[i]] = clusterCounts.GetValueOrDefault(shaped.Clusters[i]) + 1;

        var marks = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            var zeroWidth = font.MeasureText([(ushort)shaped.Codepoints[i]]) == 0;
            if (zeroWidth && clusterCounts[shaped.Clusters[i]] > 1) marks.Add(i);
        }

        return marks;
    }

    private static SKPoint F(ScenePoint p) => new((float)p.X, (float)p.Y);
}
