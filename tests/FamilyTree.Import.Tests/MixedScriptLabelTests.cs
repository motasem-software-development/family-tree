using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Import.Tests;

/// <summary>
/// Node labels are the document's entire payload, and a family member's name is free-form user
/// input that may mix scripts. <c>EmbeddedFonts.For</c> picks ONE typeface for a whole string, so
/// a label like "Ali سالم" font-selected as a single buffer resolves every Latin letter against
/// the Arabic typeface, which has no coverage for them -- they shape to glyph id 0 (.notdef) and
/// print as grey boxes.
///
/// <para>
/// <b>Why these assertions and not extracted text.</b> Skia's <c>/ToUnicode</c> CMap is hand-built
/// here from the SOURCE string (see <c>SkiaTreeRenderer.BuildShapedTextBlob</c>), so a .notdef
/// glyph still extracts as the character that was typed: every text-based assertion passes on a
/// document full of tofu. These tests therefore assert on GLYPH IDENTITY (a rendered glyph id of
/// 0 is .notdef, by the TrueType specification) and on which embedded font resource each glyph
/// was drawn with -- the two things a broken font selection actually changes.
/// </para>
/// </summary>
public sealed class MixedScriptLabelTests
{
    private const string MixedLabel = "Ali سالم";

    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    private static byte[] RenderMixedScriptTree() =>
        new TreeRendererAdapter().Render(
            [Node("سليمان", Node(MixedLabel), Node("عمر"))],
            ExportStyle.Xmind,
            "sheet",
            new PdfCaption("آل السقا", 3, 2, new DateOnly(2026, 8, 18), CaptionLanguage.Ar));

    private static PageContent Page(byte[] pdf)
    {
        var streams = PdfStreams.Inflate(pdf);
        return ContentStream.Read(PdfStreams.ContentStreamOf(streams), PdfFonts.ParseFirstPage(pdf));
    }

    /// <summary>
    /// The direct evidence: a mixed-script label's Latin glyphs and its Arabic glyphs must be
    /// drawn with DIFFERENT font resources. One typeface for the whole label is the defect
    /// itself, and it is invisible to anything that reads extracted text.
    /// </summary>
    [Fact]
    public void The_latin_and_arabic_parts_of_one_label_are_drawn_with_different_fonts()
    {
        var page = Page(RenderMixedScriptTree());

        var arabicFonts = page.Glyphs.Where(g => g.Text.Length > 0 && g.Text[0] is >= '؀' and <= 'ۿ')
            .Select(g => g.Font).Distinct().ToList();
        var latinFonts = page.Glyphs.Where(g => g.Text is "A" or "l" or "i")
            .Select(g => g.Font).Distinct().ToList();

        arabicFonts.Should().NotBeEmpty();
        latinFonts.Should().NotBeEmpty("the Latin run of the label must actually be drawn");
        latinFonts.Intersect(arabicFonts).Should().BeEmpty(
            "a run drawn with the typeface that covers the other script is exactly the defect");
    }

    /// <summary>
    /// Glyph id 0 is .notdef -- the box a TrueType font renders for a codepoint it does not
    /// cover. A correct document never draws one, because every run is font-selected against a
    /// typeface that covers it.
    /// </summary>
    [Fact]
    public void A_mixed_script_node_label_draws_no_notdef_glyphs()
    {
        var glyphs = Page(RenderMixedScriptTree()).Glyphs;

        glyphs.Should().NotBeEmpty();
        glyphs.Where(g => g.GlyphId == 0).Should().BeEmpty(
            "a glyph id of 0 is .notdef -- the label was font-selected against a typeface that "
            + "does not cover it");
    }

    /// <summary>
    /// The layout consequence of the same defect: <c>SkiaTextMeasurer</c> feeds the pure layout
    /// in Application, so measuring a mixed-script label through one typeface sizes its column
    /// from .notdef advances rather than from real glyphs. The correct width is the sum of the
    /// per-script runs plus one measured word gap between them -- the exact rule the caption
    /// already uses (<c>SkiaTreeRenderer.BuildSegmentLayouts</c>).
    /// </summary>
    [Fact]
    public void A_mixed_script_label_measures_as_the_sum_of_its_script_runs()
    {
        const double size = 13.34;

        var expected = SkiaTextMeasurer.Measure("Ali", size)
            + SkiaTextMeasurer.Measure("سالم", size)
            + SkiaTextMeasurer.Measure(" ", size);

        SkiaTextMeasurer.Measure(MixedLabel, size).Should().BeApproximately(expected, 0.001);
    }

    /// <summary>
    /// The trap in the fix above, pinned. The per-run gap is itself obtained by measuring a
    /// single space -- <c>SkiaTreeRenderer.BuildSegmentLayouts</c> has always done this for the
    /// caption, and the node-label path now does too. A splitting measurer that dropped
    /// whitespace unconditionally would return 0 here (<c>EmbeddedFonts.SplitByScript</c> drops
    /// whitespace from every run), silently collapsing every caption and every mixed-script label
    /// to zero inter-run spacing while every other assertion in this file still passed.
    /// </summary>
    [Fact]
    public void A_whitespace_only_string_still_measures_a_real_width()
    {
        SkiaTextMeasurer.Measure(" ", 13.34).Should().BeGreaterThan(0);
    }
}
