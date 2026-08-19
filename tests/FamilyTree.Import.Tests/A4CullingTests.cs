using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Import.Tests;

/// <summary>
/// End-to-end guard on per-page culling (final review, Critical 2): a real multi-page A4 export,
/// through the real adapter, must still contain everything the same tree's single-sheet export
/// contains.
///
/// <para>
/// <b>Why only the "loses nothing" half is asserted here.</b> The obvious counterpart -- "and it
/// draws much less" -- cannot be measured from the PDF, and the attempt is worth recording so it
/// is not retried. Skia's PDF backend already discards drawing that falls outside the page, so an
/// UNCULLED A4 render emits almost exactly the same operators as a culled one: measured on a
/// 1,201-member fixture before culling existed, 12 A4 pages carried 9,950 glyph draws against the
/// sheet's 9,143 -- indistinguishable from the culled result. The cost the culling removes is
/// entirely upstream of that clip (HarfBuzz shaping in <c>SkiaTreeRenderer.DrawShapedRun</c>,
/// serialised behind the process-wide shaping lock) and leaves no trace in the output at all,
/// which is precisely why the defect survived every output-based test on this branch. The
/// work-reduction half is asserted where it IS visible -- on the culling decision itself, in
/// <c>SceneCullingTests</c>.
/// </para>
/// </summary>
public sealed class A4CullingTests
{
    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    /// <summary>A tree wide and tall enough that A4 tiling produces several pages.</summary>
    private static FamilyTreeNodeResponse Fixture()
    {
        var branches = Enumerable.Range(0, 26)
            .Select(b => Node(
                $"فرع{b}",
                Enumerable.Range(0, 22).Select(c => Node($"عضو{b}-{c}")).ToArray()))
            .ToArray();

        return Node("سليمان", branches);
    }

    private static byte[] Render(string pageFormat) =>
        new TreeRendererAdapter().Render(
            [Fixture()],
            ExportStyle.Xmind,
            pageFormat,
            new PdfCaption("آل السقا", 573, 3, new DateOnly(2026, 8, 18), CaptionLanguage.Ar));

    /// <summary>
    /// Every page's content stream. A PDF content stream is textual PDF operator syntax; the
    /// embedded font programs are binary and the small <c>/ToUnicode</c> CMaps carry no drawing,
    /// so reading every text-like stream picks up all pages and nothing else.
    /// </summary>
    private static IReadOnlyList<Glyph> AllDrawnGlyphs(byte[] pdf)
    {
        var streams = PdfStreams.Inflate(pdf);
        var cmap = ToUnicodeCMap.Parse(streams);

        return streams
            .Where(IsMostlyText)
            .SelectMany(stream => ContentStream.Read(stream, cmap).Glyphs)
            .ToList();
    }

    private static bool IsMostlyText(byte[] s)
    {
        if (s.Length == 0) return false;
        var printable = s.Count(b => b is (>= 0x20 and <= 0x7E) or (byte)'\n' or (byte)'\r' or (byte)'\t');
        return printable / (double)s.Length >= 0.95;
    }

    /// <summary>
    /// If the visibility test were wrong in the losing direction -- a sign error in the page
    /// window, forgetting that the canvas translates before it scales, or node bounds that stop
    /// at the box and miss the label -- content would vanish from the printed poster silently,
    /// and every performance number would only look better for it. Comparing the SET of glyphs
    /// actually drawn against the unculled single-sheet render of the same tree is what makes
    /// that failure mode loud.
    /// </summary>
    /// <para>
    /// Counted, not set-compared. A set of glyph ids is far too coarse to notice a losing cull:
    /// this tree draws thousands of glyphs from a few dozen distinct ids, so whole names can
    /// vanish from the poster while every id still appears somewhere. A shrunken cull window
    /// that dropped 658 glyphs -- around 4% of all ink -- passed the set version of this test.
    /// Tiling may legitimately draw a glyph MORE times than one sheet does, since an item
    /// straddling a page boundary is drawn on both pages, so the assertion is one-sided.
    /// </para>
    [Fact]
    public void A_tiled_a4_export_draws_every_glyph_its_single_sheet_export_draws()
    {
        static Dictionary<int, int> CountById(IEnumerable<Glyph> glyphs) =>
            glyphs.GroupBy(g => g.GlyphId).ToDictionary(g => g.Key, g => g.Count());

        var sheet = CountById(AllDrawnGlyphs(Render("sheet")));
        var a4 = CountById(AllDrawnGlyphs(Render("a4")));

        sheet.Should().NotBeEmpty();
        foreach (var (glyphId, sheetCount) in sheet)
            a4.GetValueOrDefault(glyphId).Should().BeGreaterThanOrEqualTo(
                sheetCount, "glyph {0} is drawn {1}x on one sheet", glyphId, sheetCount);
    }
}
