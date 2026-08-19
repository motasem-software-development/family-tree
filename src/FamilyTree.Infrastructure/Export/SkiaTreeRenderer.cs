using System.Globalization;
using System.Text;
using FamilyTree.Application.Export;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FamilyTree.Infrastructure.Export;

public enum ExportPageFormat { Sheet, A4 }

public interface ITreeRenderer
{
    byte[] Render(TreeScene scene, ExportPageFormat format, PdfCaption? caption = null);
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
    private const float CaptionBandHeight = 28f;

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
    public byte[] Render(TreeScene scene, ExportPageFormat format, PdfCaption? caption = null)
    {
        using var stream = new MemoryStream();

        using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
        {
            Creator = "Family Tree",
            Title = "Family Tree"
        }))
        {
            var captionBandHeight = caption is null ? 0f : CaptionBandHeight;

            // Sheet only: a caption may need the page wider than the tree itself does. Resolved
            // BEFORE pagination, since the paginator needs to know how wide to make the one
            // page.
            CaptionLayout? sheetLayout = null;
            var sheetMinWidth = 0f;

            if (caption is not null && format == ExportPageFormat.Sheet)
            {
                var natural = ResolveCaptionLayout(caption, float.MaxValue);
                sheetLayout = natural.TotalWidth <= CaptionMaxWidth
                    ? natural
                    : ResolveCaptionLayout(caption, CaptionMaxWidth);
                sheetMinWidth = sheetLayout.TotalWidth + 2 * CaptionPageMargin;
            }

            var pages = Paginate(scene, format, captionBandHeight, sheetMinWidth).ToList();

            // Every A4 tile shares the same physical width, and there is always at least one
            // page, so the layout (font size, possible truncation) is resolved once and reused
            // -- every page's caption reads identically, and widths aren't re-measured per page.
            var layout = caption is null
                ? null
                : format == ExportPageFormat.Sheet
                    ? sheetLayout
                    : ResolveCaptionLayout(caption, pages[0].Width);

            foreach (var page in pages)
            {
                var canvas = document.BeginPage(page.Width, page.Height);
                canvas.Clear(SKColors.White);

                canvas.Save();
                if (format == ExportPageFormat.A4 && caption is not null)
                    canvas.ClipRect(SKRect.Create(page.Width, page.Height - captionBandHeight));

                canvas.Translate(-page.OffsetX + page.ContentOffsetX, -page.OffsetY);
                canvas.Scale((float)scene.Scale);

                foreach (var connector in scene.Connectors) DrawConnector(canvas, connector);
                foreach (var node in scene.Nodes) DrawNode(canvas, node);

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
    /// tree name above all needs this) and measures each run on its own. The inter-segment
    /// gap -- a genuine measured space, not a character baked into either segment's text
    /// (finding 1) -- is added once per boundary between segments, never between two runs that
    /// came from splitting the same segment: those are glued together to reconstruct that
    /// segment's own text exactly as authored.
    /// </summary>
    private static (List<CaptionSegmentLayout> Layout, float TotalWidth) BuildSegmentLayouts(
        IReadOnlyList<CaptionSegment> segments, float fontSize)
    {
        var layout = segments.Select(segment =>
        {
            var runs = EmbeddedFonts.SplitByScript(segment.Text)
                .Select(text => new CaptionRun(text, (float)SkiaTextMeasurer.Measure(text, fontSize)))
                .ToList();
            return new CaptionSegmentLayout(runs, runs.Sum(r => r.Width));
        }).ToList();

        var gap = (float)SkiaTextMeasurer.Measure(" ", fontSize);
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
    /// from the left edge forward. Either way, a segment's own runs are placed contiguously,
    /// left-to-right in their original (authored) order, regardless of the caption's overall
    /// direction -- only whole segments reorder for RTL, not the runs within one.
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
                var runX = x;
                foreach (var run in layout.Segments[i].Runs)
                {
                    yield return (run.Text, runX, run.Width);
                    runX += run.Width;
                }

                if (i < layout.Segments.Count - 1) x -= layout.Gap;
            }
        }
        else
        {
            var x = left;
            for (var i = 0; i < layout.Segments.Count; i++)
            {
                var runX = x;
                foreach (var run in layout.Segments[i].Runs)
                {
                    yield return (run.Text, runX, run.Width);
                    runX += run.Width;
                }

                x += layout.Segments[i].Width;
                if (i < layout.Segments.Count - 1) x += layout.Gap;
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
    /// Test-only seam (see AssemblyInfo.cs): the caption's computed run geometry, exactly as
    /// <see cref="DrawCaption"/> draws it -- there is no second, divergent computation. Exists
    /// because <c>/ToUnicode</c> is hand-built from the source string, so extracted PDF text
    /// cannot prove glyph position (Round-2 review) -- this proves the inter-segment gap
    /// directly instead of inferring it from text.
    /// </summary>
    internal static IReadOnlyList<(string Text, float X, float Width)> ComputeCaptionRunPositionsForTesting(
        PdfCaption caption, float pageWidth) =>
        LayoutCaptionRuns(ResolveCaptionLayout(caption, pageWidth), pageWidth).ToList();

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

    private static void DrawLabel(SKCanvas canvas, SceneNode node)
    {
        var baseline = (float)(node.Y + node.FontSize * 0.35);
        DrawShapedText(
            canvas, node.Label, (float)node.X, baseline, (float)node.FontSize, SKColors.Black);
    }

    /// <summary>
    /// Shapes and paints one line of text -- node label or bottom-margin caption alike -- at a
    /// given origin. Shared so the caption gets the same Arabic-shaping and searchability
    /// handling as every node label, rather than a second, divergent text path.
    /// </summary>
    private static void DrawShapedText(
        SKCanvas canvas, string text, float x, float baselineY, float fontSize, SKColor color)
    {
        var typeface = EmbeddedFonts.For(text);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var font = new SKFont(typeface, fontSize);

        // See EmbeddedFonts.ShapingLock for why this lock exists (concurrent shaping against a
        // shared typeface can corrupt HarfBuzz's/Skia's own caches) and why it is scoped this
        // narrowly rather than covering the whole method (a documented throughput trade-off, not
        // an oversight): only SKShaper construction, Shape, and MarkIndices's own
        // SKFont.MeasureText calls touch the shared typeface's internal tables. Everything after
        // this block -- DrawMarksAsPaths (glyph outlines onto a canvas) and canvas.DrawText (a
        // pre-built blob) -- operates on this call's own already-shaped data and this call's own
        // canvas, not on typeface-cache state, so it does not need to serialise against any
        // other concurrent render.
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
