using FamilyTree.Infrastructure.Export;
using FluentAssertions;
using SkiaSharp;

namespace FamilyTree.Application.Tests.Export;

public sealed class SkiaTextMeasurerTests
{
    [Fact]
    public void The_embedded_arabic_typeface_loads()
    {
        EmbeddedFonts.Arabic.Should().NotBeNull();
        EmbeddedFonts.Arabic.FamilyName.Should().Contain("Noto");
    }

    [Fact]
    public void A_measured_label_has_positive_width()
    {
        SkiaTextMeasurer.Measure("سليمان", 13.34).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Width_scales_with_font_size()
    {
        var small = SkiaTextMeasurer.Measure("سليمان", 13.34);
        var large = SkiaTextMeasurer.Measure("سليمان", 26.68);

        large.Should().BeGreaterThan(small);
    }

    // Arabic is cursive: joined forms are narrower than the same letters separated. Without
    // shaping this comes out the other way round, which is exactly the bug this test catches.
    // Note: this alone doesn't prove HarfBuzz ran — the "separated" string simply carries five
    // extra space-glyph advances regardless of shaping, so the letter glyphs could be identical
    // in both cases and this would still pass. See
    // Shaping_narrows_arabic_relative_to_the_unshaped_measurement below for the guard that
    // actually catches a bypassed shaper.
    [Fact]
    public void Shaping_is_applied_so_a_joined_word_is_narrower_than_its_separated_letters()
    {
        var joined = SkiaTextMeasurer.Measure("سليمان", 13.34);
        var separated = SkiaTextMeasurer.Measure("س ل ي م ا ن", 13.34);

        joined.Should().BeLessThan(separated);
    }

    // The only guard that HarfBuzz is actually in the path. Same string measured both ways, so
    // no difference in glyph count or space advances can mask a bypassed shaper: cursive joining
    // (GSUB substitution to joined glyph forms) is the only thing that can make the shaped
    // measurement narrower than the unshaped one, since cmap alone maps every codepoint to its
    // isolated form. Measured: unshaped ~59.95, shaped ~45.13 (at font size 13.34).
    [Fact]
    public void Shaping_narrows_arabic_relative_to_the_unshaped_measurement()
    {
        const string word = "سليمان";

        var shaped = SkiaTextMeasurer.Measure(word, 13.34);

        using var font = new SKFont(EmbeddedFonts.Arabic, 13.34f);
        using var paint = new SKPaint();
        var unshaped = font.MeasureText(word, paint);

        shaped.Should().BeLessThan(unshaped);
    }

    [Fact]
    public void Latin_text_measures_too()
    {
        SkiaTextMeasurer.Measure("Suleiman", 13.34).Should().BeGreaterThan(0);
    }
}
