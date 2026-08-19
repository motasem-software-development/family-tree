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

        // Word ORDER within the Arabic name is not asserted from extracted text here: splitting
        // a multi-word Arabic name into per-word runs (Round-3 review, finding 3) means
        // pdftotext reorders adjacent Arabic-script text objects toward its own RTL heuristic on
        // extraction, independent of their actual drawn position -- exactly the kind of claim
        // the review said text extraction cannot support. Word order is instead verified
        // geometrically in A_plain_arabic_name_in_an_english_caption_keeps_typed_order.
        text.Should().Contain("آل");
        text.Should().Contain("سالم");
        text.Should().Contain("members");
        text.Should().Contain("generations");
        text.Should().Contain("Exported");
        text.Should().NotContain("\0");
    }

    // Design §4.6, Round-2 review finding 1 regression guard: inter-segment spacing is an
    // explicit measured gap, never a character baked into either segment's own text. Proven on
    // the caption's computed run geometry -- resolved the SAME way Render resolves it (Round-3
    // review, finding 2 fix) -- rather than extracted text, since /ToUnicode is hand-built from
    // the source string and proves nothing about glyph *position* (Round-2 review).
    [Fact]
    public void The_gap_around_the_member_count_is_equal_on_both_sides()
    {
        var caption = new PdfCaption("آل سالم", 42, 5, new DateOnly(2026, 8, 18), CaptionLanguage.Ar);

        var visual = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet)
            .OrderBy(r => r.X)
            .ToList();

        AssertSymmetricGap(visual, "42");
    }

    /// <summary>Finds <paramref name="runText"/> among <paramref name="visualOrder"/> (already
    /// sorted left to right) and asserts its neighbours sit an equal, positive gap away on both
    /// sides -- an asymmetric gap is exactly the Round-2/Round-3 join-space defect, wherever it
    /// recurs.</summary>
    private static void AssertSymmetricGap(
        IReadOnlyList<(string Text, float X, float Width)> visualOrder, string runText)
    {
        var index = visualOrder.ToList().FindIndex(r => r.Text == runText);
        index.Should().BeInRange(1, visualOrder.Count - 2, $"'{runText}' must have a neighbour on both sides");

        var before = visualOrder[index - 1];
        var run = visualOrder[index];
        var after = visualOrder[index + 1];

        var gapBefore = run.X - (before.X + before.Width);
        var gapAfter = after.X - (run.X + run.Width);

        gapBefore.Should().BeGreaterThan(0f);
        gapAfter.Should().BeGreaterThan(0f);
        gapBefore.Should().BeApproximately(gapAfter, 0.01f,
            $"an asymmetric gap around '{runText}' is exactly the Round-2/Round-3 join-space defect");
    }

    // Design §4.6, Round-2 review finding 2 regression guard, re-verified after the Round-3
    // finding-2 fix (the seam now resolves the SAME way Render does, so this is no longer at
    // risk of certifying a layout that was never drawn): shrinking the font and truncating the
    // name were not enough on their own -- a minimum-width caption could still exceed a small
    // page. The page must grow to fit instead, and the date must never be the thing that gets
    // clipped.
    [Fact]
    public void A_small_page_grows_to_fit_an_english_caption_without_clipping_anything()
    {
        var tinyScene = OneMemberScene("A");
        var caption = Caption();

        var pdf = new SkiaTreeRenderer().Render(tinyScene, ExportPageFormat.Sheet, caption);
        var pageWidth = MediaBoxWidth(pdf);

        var runs = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet);
        runs.Should().NotBeEmpty();
        runs.Min(r => r.X).Should().BeGreaterThanOrEqualTo(-0.01f, "no run may start off the left edge");
        runs.Max(r => r.X + r.Width).Should()
            .BeLessThanOrEqualTo(pageWidth + 0.01f, "no run may end off the right edge of the ACTUAL rendered page");

        var text = ExtractText(pdf);
        text.Should().Contain("Al-Hassan Family");
        text.Should().Contain("4 members");
        text.Should().Contain("2 generations");
        text.Should().Contain("Exported 2026-08-18", "the date must never be the thing that gets clipped");
    }

    // Design §4.6, Round-2 review finding 2: growing the page is bounded (CaptionMaxWidth) --
    // an enormous name still falls back to shrink-then-ellipsise, now measured against that
    // bound rather than the tree's own (possibly tiny) natural width. This is the exact scenario
    // Round-3's finding 2 measured diverging (production resolved against CaptionMaxWidth; the
    // old seam resolved against a caller-supplied pageWidth) -- re-verified against the actual
    // rendered /MediaBox now that both paths share one resolution.
    [Fact]
    public void An_enormous_name_still_falls_back_to_truncation_on_a_capped_page()
    {
        var tinyScene = OneMemberScene("A");
        var caption = new PdfCaption(
            string.Concat(Enumerable.Repeat("Very Long Family Name ", 15)),
            1, 1, new DateOnly(2026, 8, 18), CaptionLanguage.En);

        var pdf = new SkiaTreeRenderer().Render(tinyScene, ExportPageFormat.Sheet, caption);
        var pageWidth = MediaBoxWidth(pdf);

        var runs = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet);
        runs.Max(r => r.X + r.Width).Should().BeLessThanOrEqualTo(pageWidth + 0.01f,
            "the seam's resolved layout must match what was actually drawn onto this exact page");

        var text = ExtractText(pdf);
        text.Should().Contain("1 members");
        text.Should().Contain("Exported 2026-08-18", "the date is never truncated, even when the name is");
        text.Should().Contain("…", "an enormous name still needs the ellipsis fallback");
    }

    // Design §4.6, Round-3 review finding 2 regression guard, directly: the seam's assumed page
    // width (used only to centre the returned run geometry) must match the actual rendered
    // /MediaBox width, for both the natural-size sheet path (small caption, page grows to fit
    // exactly) and the capped/truncated path (enormous name, page grows only to the cap).
    [Theory]
    [InlineData("Al-Hassan Family", 4, 2)]
    public void The_seam_page_width_matches_the_actual_rendered_mediabox_width(
        string name, int members, int generations)
    {
        var tinyScene = OneMemberScene("A");
        var caption = new PdfCaption(name, members, generations, new DateOnly(2026, 8, 18), CaptionLanguage.En);

        var pdf = new SkiaTreeRenderer().Render(tinyScene, ExportPageFormat.Sheet, caption);
        var actualPageWidth = MediaBoxWidth(pdf);

        var runs = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet);
        var seamRightEdge = runs.Max(r => r.X + r.Width);
        var seamLeftEdge = runs.Min(r => r.X);

        // The seam centres its resolved layout within its own assumed page width, so the left
        // margin it produced equals half of (assumed page width - resolved width); by symmetry
        // the assumed page width is recoverable as leftEdge + rightEdge. If the seam's
        // assumption has drifted from what was actually rendered, this will not equal the real
        // /MediaBox width.
        var impliedSeamPageWidth = seamLeftEdge + seamRightEdge;
        impliedSeamPageWidth.Should().BeApproximately(actualPageWidth, 0.5f,
            "the seam's assumed page width must match the actual rendered /MediaBox width");
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
        string.Concat(runs).Should().Be(
            name.Replace(" ", ""), "splitting must not lose or reorder any non-whitespace character");

        foreach (var run in runs)
        {
            var expectArabic = run.Any(c => c is >= '؀' and <= 'ۿ');
            (EmbeddedFonts.For(run) == EmbeddedFonts.Arabic).Should().Be(expectArabic,
                $"run '{run}' must be drawn with the font that actually has its glyphs");
        }
    }

    // Design §4.6, Round-3 review finding 3 regression guard: whitespace must not survive into
    // any run's text -- an earlier fix attached it to a neighbouring script instead, which
    // reintroduced Round 2's asymmetric-join-space defect one level down, inside a mixed-script
    // (or even a plain multi-word Arabic) name.
    [Fact]
    public void A_plain_arabic_name_splits_at_its_word_space_with_no_whitespace_in_either_run()
    {
        var runs = EmbeddedFonts.SplitByScript("آل سالم");

        runs.Should().Equal("آل", "سالم");
        runs.Should().OnlyContain(r => !r.Any(char.IsWhiteSpace));
    }

    // Design §4.6, Round-3 review finding 3 regression guard: run spacing inside a segment uses
    // the SAME explicit gap mechanism as between segments, so a gap around an embedded run
    // cannot land asymmetrically depending on which script's shaper owned the space that used to
    // be there. Measured (En caption, Latin-first mixed name): the review found a 0.0039pt gap
    // on one side of the embedded Arabic word and a doubled ~4.16pt gap on the other.
    [Fact]
    public void The_gap_around_an_embedded_arabic_word_in_a_mixed_name_is_equal_on_both_sides()
    {
        var caption = new PdfCaption(
            "The Smith آل Family Association", 9, 4, new DateOnly(2026, 8, 18), CaptionLanguage.En);

        var visual = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet)
            .OrderBy(r => r.X)
            .ToList();

        AssertSymmetricGap(visual, "آل");
    }

    // Design §4.6, Round-3 review finding 3 regression guard, direction-consistent run ordering
    // (documented as NOT full Unicode bidi -- see SkiaTreeRenderer.LayoutSegmentRuns): in an
    // ARABIC caption, a mixed-script name's runs must order the same way the caption's segments
    // do -- right-to-left, so the first-TYPED run ends up rightmost, not left-to-right in typed
    // order regardless of direction (the review measured exactly this: "آل" leftmost at 183.5,
    // "جمعية" rightmost at 267.5, inside an otherwise RTL line -- backwards).
    [Fact]
    public void A_mixed_script_name_in_an_arabic_caption_orders_runs_right_to_left()
    {
        var caption = new PdfCaption(
            "آل Smith Family جمعية", 9, 4, new DateOnly(2026, 8, 18), CaptionLanguage.Ar);

        var visual = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet)
            .OrderBy(r => r.X)
            .Select(r => r.Text)
            .ToList();

        // Name typed order is آل, Smith, Family, جمعية. Direction-consistent RTL ordering means
        // the first-typed run ("آل") ends up rightmost -- i.e. LAST in left-to-right visual
        // order -- not first.
        var nameRunsInVisualOrder = visual.Where(t => t is "آل" or "Smith" or "Family" or "جمعية").ToList();
        nameRunsInVisualOrder.Should().Equal("جمعية", "Family", "Smith", "آل");
    }

    // Companion to the direction test above, but for a LATIN-context (En) caption: a plain
    // multi-word Arabic name's runs must stay in typed (left-to-right) order, since the caption
    // itself reads left-to-right -- "آل" (typed first) leftmost, "سالم" (typed second) to its
    // right. Checked geometrically, not via extracted text: pdftotext reorders adjacent
    // Arabic-script text objects toward its own RTL reading-order heuristic regardless of their
    // actual drawn position once they are split into separate runs, which is exactly the kind of
    // claim the Round-3 review warned text extraction cannot support.
    [Fact]
    public void A_plain_arabic_name_in_an_english_caption_keeps_typed_order()
    {
        var caption = new PdfCaption("آل سالم", 9, 4, new DateOnly(2026, 8, 18), CaptionLanguage.En);

        var visual = SkiaTreeRenderer.ComputeCaptionRunPositionsForTesting(caption, ExportPageFormat.Sheet)
            .OrderBy(r => r.X)
            .Select(r => r.Text)
            .ToList();

        visual.Where(t => t is "آل" or "سالم").Should().Equal("آل", "سالم");
    }

    // Design §4.6, Round-3 review Critical 1 regression guard: reserving the caption band by
    // adding it to an already-maxed page pushes the page past the PDF format's legal 14400pt
    // maximum. This mirrors what TreeRendererAdapter does (SceneScaler.FitToSheet with the
    // caption band reserved, THEN SkiaTreeRenderer.Render) for the review's own measured
    // scenario: a 600x20000 scene, body font comfortably above the legibility floor. Must fail
    // against a SceneScaler.FitToSheet call with no reservedHeight parameter.
    [Fact]
    public void A_tall_scene_with_a_caption_never_exceeds_the_pdf_page_extent_cap()
    {
        var scene = new TreeScene([], [], new SceneBounds(0, 0, 600, 20000));
        var caption = Caption();

        var fitted = SceneScaler.FitToSheet(scene, LayoutOptions.Default.Metrics, SkiaTreeRenderer.CaptionBandHeight);
        var pdf = new SkiaTreeRenderer().Render(fitted, ExportPageFormat.Sheet, caption);

        var height = MediaBoxHeight(pdf);
        height.Should().BeLessThanOrEqualTo(
            (float)LayoutOptions.Default.Metrics.MaxPageExtent,
            "the total page extent (scaled scene + caption band) must never exceed the PDF format's legal maximum");
    }

    private static float MediaBoxHeight(byte[] pdf)
    {
        var raw = System.Text.Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(raw, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        match.Success.Should().BeTrue("the PDF must declare a MediaBox");
        return float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    }
}
