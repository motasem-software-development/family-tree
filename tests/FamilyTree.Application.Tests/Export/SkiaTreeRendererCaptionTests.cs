using System.Text.RegularExpressions;
using FamilyTree.Application.Export;
using FamilyTree.Contracts.FamilyTrees;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

/// <summary>Design §4.6: a restrained bottom-margin caption, drawn outside the tree's own
/// scale so it never grows or shrinks with it, using the export date threaded in as a value
/// rather than read from the clock inside rendering.</summary>
public sealed class SkiaTreeRendererCaptionTests
{
    private static FamilyTreeNodeResponse Tree()
    {
        FamilyTreeNodeResponse Leaf(string name) => new(Guid.NewGuid(), name, null, 2, false, []);

        return new FamilyTreeNodeResponse(
            Guid.NewGuid(), "root", null, 1, false,
            [
                new FamilyTreeNodeResponse(Guid.NewGuid(), "alpha", null, 2, false, [Leaf("a1")]),
                new FamilyTreeNodeResponse(Guid.NewGuid(), "beta", null, 2, false, [Leaf("b1")])
            ]);
    }

    private static TreeScene Scene() =>
        SceneScaler.FitToSheet(
            new XmindLayoutStrategy().Build([Tree()], LayoutOptions.Default, SkiaTextMeasurer.Delegate),
            LayoutOptions.Default.Metrics);

    /// <summary>Bypasses layout entirely: an empty, deliberately large scene, so A4 tiling
    /// produces several pages without needing a real tree to fill them (mirrors
    /// A4PaginatorTests' own approach).</summary>
    private static TreeScene LargeScene() => new([], [], new SceneBounds(0, 0, 1400, 2000));

    private static PdfCaption Caption() => new(
        "Al-Hassan Family", 4, 2, new DateOnly(2026, 8, 18), CaptionLanguage.En);

    private static string ExtractText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ft-caption-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, pdf);
            return PdfText.Extract(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_sheet_export_carries_the_caption_in_its_text_layer()
    {
        var pdf = new SkiaTreeRenderer().Render(Scene(), ExportPageFormat.Sheet, Caption());

        var text = ExtractText(pdf);

        text.Should().Contain("Al-Hassan Family");
        text.Should().Contain("2026-08-18");
    }

    [Fact]
    public void No_caption_is_drawn_when_none_is_supplied()
    {
        var pdf = new SkiaTreeRenderer().Render(Scene(), ExportPageFormat.Sheet);

        ExtractText(pdf).Should().NotContain("Al-Hassan Family");
    }

    // The export date is threaded in as a value on PdfCaption, never read from the clock inside
    // rendering -- so two renders given the same caption produce identical bytes.
    [Fact]
    public void Rendering_the_same_scene_and_caption_twice_produces_identical_bytes()
    {
        var scene = Scene();
        var caption = Caption();

        new SkiaTreeRenderer().Render(scene, ExportPageFormat.Sheet, caption)
            .Should().Equal(new SkiaTreeRenderer().Render(scene, ExportPageFormat.Sheet, caption));
    }

    // Design §4.6, Important 4 fix: every A4 tile reserves its own caption band, so every tile
    // (not just the last) carries the caption. LargeScene forces several tiles.
    [Fact]
    public void Every_a4_page_carries_the_caption_not_only_the_last()
    {
        var pdf = new SkiaTreeRenderer().Render(LargeScene(), ExportPageFormat.A4, Caption());

        var text = ExtractText(pdf);
        var occurrences = Regex.Matches(text, Regex.Escape("Al-Hassan Family")).Count;

        occurrences.Should().BeGreaterThan(1, "the scene tiles across more than one A4 page");
    }

    // Design §4.6, Critical 1/2 fix regression guard: the DEFAULT path (Ar language, Arabic tree
    // name) is the one whole-string shaping got wrong -- digits and the ISO date reversed, and
    // Latin/Arabic glyphs landed on the wrong font. Segmenting by script is what a mixed Latin
    // digit + Arabic word buffer needs; this must be proven on exactly that input, not on an
    // all-Latin caption where the bug cannot show up.
    [Fact]
    public void The_default_arabic_caption_keeps_digits_and_the_date_in_reading_order()
    {
        var caption = new PdfCaption("آل سالم", 17, 3, new DateOnly(2026, 8, 18), CaptionLanguage.Ar);
        var pdf = new SkiaTreeRenderer().Render(Scene(), ExportPageFormat.Sheet, caption);

        var text = ExtractText(pdf);

        text.Should().Contain("آل سالم");
        text.Should().Contain("17", "the member count must not come out reversed (e.g. '71')");
        text.Should().Contain("3", "the generation count must survive");
        text.Should().Contain("2026-08-18", "the ISO date must not come out mirrored");
        text.Should().NotContain("6202-80-81", "a reversed date is exactly the Critical 1 defect");
        text.Should().Contain("أفراد", "the Arabic label must render with real glyphs, not tofu");
        text.Should().Contain("أجيال");
        text.Should().NotContain("\0", "a codepoint the wrong font can't map surfaces as U+0000");
    }

    // Design §4.6, Critical 2 fix regression guard: the mixed case -- an English caption whose
    // tree name is Arabic -- is what exposed whole-string font selection (one Arabic character
    // picked the Arabic typeface for the entire caption, so "members"/"generations"/"Exported"
    // -- none of which exist in that font -- rendered as tofu).
    [Fact]
    public void An_english_caption_with_an_arabic_tree_name_renders_both_scripts()
    {
        var caption = new PdfCaption("آل سالم", 9, 4, new DateOnly(2026, 8, 18), CaptionLanguage.En);
        var pdf = new SkiaTreeRenderer().Render(Scene(), ExportPageFormat.Sheet, caption);

        var text = ExtractText(pdf);

        text.Should().Contain("آل سالم");
        text.Should().Contain("members");
        text.Should().Contain("generations");
        text.Should().Contain("Exported");
        text.Should().NotContain("\0");
    }

    // Design §4.6, Important 3 fix: a caption wider than the page shrinks toward the 6pt floor
    // and, failing that, truncates the NAME (never the counts or date) with an ellipsis -- a
    // tiny one-member tree with a long name is exactly the scenario the review measured as
    // clipped off both edges.
    [Fact]
    public void A_caption_wider_than_a_small_page_is_shrunk_and_the_name_is_truncated()
    {
        var tinyTree = new FamilyTreeNodeResponse(Guid.NewGuid(), "Ahmad Al-Sayed", null, 1, false, []);
        var tinyScene = SceneScaler.FitToSheet(
            new XmindLayoutStrategy().Build(
                [tinyTree], LayoutOptions.Default, SkiaTextMeasurer.Delegate),
            LayoutOptions.Default.Metrics);

        var caption = new PdfCaption(
            "A Very Long Family Name That Will Not Fit On A One-Member Sheet",
            1, 1, new DateOnly(2026, 8, 18), CaptionLanguage.En);

        var pdf = new SkiaTreeRenderer().Render(tinyScene, ExportPageFormat.Sheet, caption);

        var text = ExtractText(pdf);

        // The counts and date are never truncated -- only the name is.
        text.Should().Contain("1 members");
        text.Should().Contain("Exported 2026-08-18");
        text.Should().Contain("…", "the name had to be shortened to fit the page");
    }
}
