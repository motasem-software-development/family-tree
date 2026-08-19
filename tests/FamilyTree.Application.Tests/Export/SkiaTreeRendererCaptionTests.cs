using System.Globalization;
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

    private static TreeScene OneMemberScene(string name)
    {
        var tree = new FamilyTreeNodeResponse(Guid.NewGuid(), name, null, 1, false, []);
        return SceneScaler.FitToSheet(
            new XmindLayoutStrategy().Build([tree], LayoutOptions.Default, SkiaTextMeasurer.Delegate),
            LayoutOptions.Default.Metrics);
    }

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

    /// <summary>The physical page width straight from the PDF's own <c>/MediaBox</c> -- used to
    /// check the caption's computed run geometry actually fits the page that was produced, not
    /// just a page width computed independently.</summary>
    private static float MediaBoxWidth(byte[] pdf)
    {
        var raw = System.Text.Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(raw, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        match.Success.Should().BeTrue("the PDF must declare a MediaBox");
        return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
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

    // Design §4.6, Round-2 review finding 1 regression guard: inter-segment spacing is an
    // explicit measured gap, never a character baked into either segment's own text. Proven on
    // the caption's computed run geometry -- the exact numbers DrawCaption draws with -- rather
    // than extracted text, since /ToUnicode is hand-built from the source string and proves
    // nothing about glyph *position* (Round-2 review).
    [Fact]
    public void The_gap_around_the_member_count_is_equal_on_both_sides()
    {
        var caption = new PdfCaption("آل سالم", 42, 5, new DateOnly(2026, 8, 18), CaptionLanguage.Ar);

        // A page far wider than the caption's natural size, so no shrink/truncate branch can
        // interfere with this measurement.
        var visual = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, 2000f)
            .OrderBy(r => r.X)
            .ToList();

        var digitIndex = visual.FindIndex(r => r.Text == "42");
        digitIndex.Should().BeInRange(1, visual.Count - 2, "the digit run must have a neighbour on both sides");

        var before = visual[digitIndex - 1];
        var digit = visual[digitIndex];
        var after = visual[digitIndex + 1];

        var gapBefore = digit.X - (before.X + before.Width);
        var gapAfter = after.X - (digit.X + digit.Width);

        gapBefore.Should().BeGreaterThan(0f);
        gapAfter.Should().BeGreaterThan(0f);
        gapBefore.Should().BeApproximately(gapAfter, 0.01f,
            "an asymmetric gap around the digit run is exactly the Round-2 defect");
    }

    // Design §4.6, Round-2 review finding 2 regression guard: shrinking the font and truncating
    // the name were not enough on their own -- a minimum-width caption could still exceed a
    // small page. The page must grow to fit instead, and the date must never be the thing that
    // gets clipped.
    [Fact]
    public void A_small_page_grows_to_fit_an_english_caption_without_clipping_anything()
    {
        var tinyScene = OneMemberScene("A");
        var caption = Caption();

        var pdf = new SkiaTreeRenderer().Render(tinyScene, ExportPageFormat.Sheet, caption);
        var pageWidth = MediaBoxWidth(pdf);

        var runs = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, pageWidth);
        runs.Should().NotBeEmpty();
        runs.Min(r => r.X).Should().BeGreaterThanOrEqualTo(0f, "no run may start off the left edge");
        runs.Max(r => r.X + r.Width).Should()
            .BeLessThanOrEqualTo(pageWidth, "no run may end off the right edge");

        var text = ExtractText(pdf);
        text.Should().Contain("Al-Hassan Family");
        text.Should().Contain("4 members");
        text.Should().Contain("2 generations");
        text.Should().Contain("Exported 2026-08-18", "the date must never be the thing that gets clipped");
    }

    // Design §4.6, Round-2 review finding 2: growing the page is bounded (CaptionMaxWidth) --
    // an enormous name still falls back to shrink-then-ellipsise, now measured against that
    // bound rather than the tree's own (possibly tiny) natural width.
    [Fact]
    public void An_enormous_name_still_falls_back_to_truncation_on_a_capped_page()
    {
        var tinyScene = OneMemberScene("A");
        var caption = new PdfCaption(
            string.Concat(Enumerable.Repeat("Very Long Family Name ", 15)),
            1, 1, new DateOnly(2026, 8, 18), CaptionLanguage.En);

        var pdf = new SkiaTreeRenderer().Render(tinyScene, ExportPageFormat.Sheet, caption);
        var pageWidth = MediaBoxWidth(pdf);

        var runs = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, pageWidth);
        runs.Max(r => r.X + r.Width).Should().BeLessThanOrEqualTo(pageWidth + 0.5f);

        var text = ExtractText(pdf);
        text.Should().Contain("1 members");
        text.Should().Contain("Exported 2026-08-18", "the date is never truncated, even when the name is");
        text.Should().Contain("…", "an enormous name still needs the ellipsis fallback");
    }

    // Design §4.6, Round-2 review finding 3 regression guard: the family tree name is free-form
    // user input and may itself mix Arabic and Latin. Checked on FONT SELECTION per run, not on
    // extracted text -- /ToUnicode is hand-built from the source string, so extracted text looks
    // identical whether or not the right glyphs were actually drawn (Round-2 review).
    [Fact]
    public void A_mixed_script_name_splits_into_runs_each_on_its_own_correct_font()
    {
        const string name = "The Smith آل Family Association";
        var runs = EmbeddedFonts.SplitByScript(name);

        runs.Should().HaveCountGreaterThan(1, "the name mixes Latin and Arabic");
        string.Concat(runs).Should().Be(name, "splitting must not lose or reorder any character");

        foreach (var run in runs)
        {
            var expectArabic = run.Any(c => c is >= '؀' and <= 'ۿ');
            (EmbeddedFonts.For(run) == EmbeddedFonts.Arabic).Should().Be(expectArabic,
                $"run '{run}' must be drawn with the font that actually has its glyphs");
        }
    }

    // A plain multi-word Arabic name (no Latin at all) must stay a single run: the
    // word-separating space is not itself in any Arabic code range, but treating it as a script
    // boundary would make the caption's run layout -- which glues a segment's own runs together
    // left to right in encounter order -- silently swap the two words (found while fixing
    // finding 3).
    [Fact]
    public void A_plain_arabic_name_with_a_space_does_not_split_at_all()
    {
        EmbeddedFonts.SplitByScript("آل سالم").Should().ContainSingle().Which.Should().Be("آل سالم");
    }
}
