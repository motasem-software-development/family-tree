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
    private const float CaptionBottomPadding = 10f;

    // The palette's own centre grey (design LayoutOptions.BranchPalette.CentreColor):
    // restrained, not attention-seeking -- this is furniture, not data (design §4.6).
    private static readonly SKColor CaptionColor = SKColor.Parse("#8793A5");

    /// <param name="caption">
    /// Null draws no caption. When present it is drawn in the bottom margin, in device points,
    /// outside the scene's own Translate/Scale -- it must not shrink or grow with the tree
    /// (design §4.6). Sheet format captions its one page; A4 captions only its last page, since
    /// every-page captioning would need paginator geometry changes this task did not make.
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
            var pages = Paginate(scene, format).ToList();

            for (var i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                var canvas = document.BeginPage(page.Width, page.Height);
                canvas.Clear(SKColors.White);

                canvas.Save();
                canvas.Translate(-page.OffsetX, -page.OffsetY);
                canvas.Scale((float)scene.Scale);

                foreach (var connector in scene.Connectors) DrawConnector(canvas, connector);
                foreach (var node in scene.Nodes) DrawNode(canvas, node);

                canvas.Restore();

                var isLastPage = i == pages.Count - 1;
                if (caption is not null && (format == ExportPageFormat.Sheet || isLastPage))
                    DrawCaption(canvas, page, caption);

                document.EndPage();
            }

            document.Close();
        }

        return stream.ToArray();
    }

    private static void DrawCaption(SKCanvas canvas, PageWindow page, PdfCaption caption)
    {
        var text = CaptionLocalizer.Format(caption);
        var width = (float)SkiaTextMeasurer.Measure(text, CaptionFontSize);
        var x = (page.Width - width) / 2f;
        var baseline = page.Height - CaptionBottomPadding;

        DrawShapedText(canvas, text, x, baseline, CaptionFontSize, CaptionColor);
    }

    private static IEnumerable<PageWindow> Paginate(TreeScene scene, ExportPageFormat format) =>
        format switch
        {
            ExportPageFormat.Sheet => SheetPaginator.Pages(scene),
            ExportPageFormat.A4 => A4Paginator.Pages(scene),
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
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var shaper = new SKShaper(typeface);

        var shaped = shaper.Shape(text, x, baselineY, font);

        // Shaping is what joins Arabic correctly, but two things stand between a shaped glyph
        // run and a *searchable* one:
        //
        // 1. SKCanvas.DrawShapedText derives its /ToUnicode CMap by reverse-scanning the font's
        //    own cmap table from glyph id back to a codepoint — and Noto Sans Arabic's cmap also
        //    carries legacy presentation-form entries that win that scan, so pdftotext recovers
        //    glyph-shape codepoints (e.g. U+FE8E) or U+0000, not the letters that were actually
        //    typed. A hand-built text blob that attaches the source UTF-8 text and HarfBuzz's
        //    cluster map to the run instead makes Skia's PDF backend build /ToUnicode from the
        //    real text.
        //
        // 2. Some letters (e.g. the dot on ف or ي) shape to a base glyph plus a zero-advance
        //    mark positioned via GPOS. Every attempt to keep that mark in the text layer —
        //    interleaved with its base, reordered by X, or drawn as its own zero-width text run
        //    — leaves either a stray character or a spurious mid-word space in pdftotext's
        //    output, because the mark's presence as a *text* object (however it's ordered or
        //    associated) still perturbs pdftotext's spatial word-gap reconstruction. Painting
        //    marks as plain vector paths instead removes them from the text layer entirely:
        //    invisible to every extractor, and the remaining base-glyph run is a clean,
        //    monotonic sequence that matches the font's own declared advances.
        //
        // Do not swap this back for DrawShapedText, and do not fold the marks back into the
        // text run, without re-running the searchability gate.
        var marks = MarkIndices(shaped, font);
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
