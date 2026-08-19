using FamilyTree.Infrastructure.Export;
using FluentAssertions;

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
    [Fact]
    public void Shaping_is_applied_so_a_joined_word_is_narrower_than_its_separated_letters()
    {
        var joined = SkiaTextMeasurer.Measure("سليمان", 13.34);
        var separated = SkiaTextMeasurer.Measure("س ل ي م ا ن", 13.34);

        joined.Should().BeLessThan(separated);
    }

    [Fact]
    public void Latin_text_measures_too()
    {
        SkiaTextMeasurer.Measure("Suleiman", 13.34).Should().BeGreaterThan(0);
    }
}
