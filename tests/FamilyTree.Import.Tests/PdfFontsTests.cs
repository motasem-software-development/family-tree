using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Import.Tests;

/// <summary>
/// Per-font <c>/ToUnicode</c> keying (final review, Important 3).
///
/// <para>
/// Every production export carries a caption, and a caption embeds a second font. Identity-H
/// subsets number their glyphs from zero, so two embedded subsets have completely overlapping
/// glyph-id spaces -- and <see cref="ToUnicodeCMap.Parse(IEnumerable{byte[]})"/> merges all CMap
/// streams into one flat dictionary, so whichever stream is read last wins every collision.
/// </para>
/// </summary>
public sealed class PdfFontsTests
{
    private static FamilyTreeNodeResponse Node(string name, params FamilyTreeNodeResponse[] children) =>
        new(Guid.NewGuid(), name, null, 1, false, children);

    /// <summary>A captioned export: Arabic node labels in one font, a Latin caption in another.</summary>
    private static byte[] CaptionedExport(CaptionLanguage language) =>
        new TreeRendererAdapter().Render(
            [Node("سليمان", Node("أحمد"), Node("عمر"))],
            ExportStyle.Xmind,
            "sheet",
            new PdfCaption("Al Saqqa", 3, 2, new DateOnly(2026, 8, 18), language));

    [Fact]
    public void A_captioned_export_declares_more_than_one_font_resource()
    {
        var fonts = PdfFonts.ParseFirstPage(CaptionedExport(CaptionLanguage.En));

        fonts.ResourceNames.Should().HaveCountGreaterThan(1,
            "node labels and a Latin caption cannot share one embedded subset");
    }

    /// <summary>
    /// The collision itself, shown rather than argued. Two subsets both number glyphs from zero,
    /// so the same low glyph id means different text in each -- and the merged map can only hold
    /// one answer for it.
    /// </summary>
    [Fact]
    public void Two_embedded_subsets_disagree_about_what_the_same_glyph_id_means()
    {
        var pdf = CaptionedExport(CaptionLanguage.En);
        var fonts = PdfFonts.ParseFirstPage(pdf);

        var maps = fonts.ResourceNames.Select(fonts.For).OfType<ToUnicodeCMap>().ToList();
        maps.Should().HaveCountGreaterThan(1);

        var disagreements = Enumerable.Range(0, 512)
            .Select(id => maps.Select(m => m.Lookup(id)).Where(t => t is not null).Distinct().Count())
            .Count(distinct => distinct > 1);

        disagreements.Should().BeGreaterThan(0,
            "if the two subsets' glyph ids never collided, merging their maps would be harmless");
    }

    /// <summary>
    /// The merged map is measurably lossy: it holds strictly fewer entries than the per-font maps
    /// hold between them, and every missing entry is one font's meaning overwritten by another's.
    /// </summary>
    [Fact]
    public void Merging_every_cmap_stream_loses_entries_that_per_font_keying_keeps()
    {
        var pdf = CaptionedExport(CaptionLanguage.En);

        var fonts = PdfFonts.ParseFirstPage(pdf);

        var merged = ToUnicodeCMap.Parse(PdfStreams.Inflate(pdf)).Count;
        var perFont = fonts.ResourceNames.Select(fonts.For).OfType<ToUnicodeCMap>().Sum(m => m.Count);

        merged.Should().BeLessThan(perFont);
    }

    /// <summary>
    /// A glyph must carry the font that drew it, or nothing downstream can tell which map to
    /// decode it against.
    /// </summary>
    [Fact]
    public void Every_drawn_glyph_records_the_font_resource_it_was_drawn_with()
    {
        var pdf = CaptionedExport(CaptionLanguage.Ar);
        var streams = PdfStreams.Inflate(pdf);
        var page = ContentStream.Read(PdfStreams.ContentStreamOf(streams), PdfFonts.ParseFirstPage(pdf));

        page.Glyphs.Should().NotBeEmpty();
        page.Glyphs.Should().OnlyContain(g => g.Font != "");
    }
}
