using FamilyTree.Application.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class CaptionLocalizerTests
{
    private static PdfCaption Caption(CaptionLanguage language) => new(
        FamilyTreeName: "آل سالم",
        MemberCount: 42,
        GenerationCount: 5,
        ExportDate: new DateOnly(2026, 8, 18),
        Language: language);

    [Fact]
    public void The_arabic_caption_carries_the_name_counts_and_date()
    {
        var text = CaptionLocalizer.Format(Caption(CaptionLanguage.Ar));

        text.Should().Contain("آل سالم");
        text.Should().Contain("42");
        text.Should().Contain("5");
        text.Should().Contain("2026-08-18");
    }

    [Fact]
    public void The_english_caption_carries_the_name_counts_and_date()
    {
        var text = CaptionLocalizer.Format(Caption(CaptionLanguage.En));

        text.Should().Contain("آل سالم");
        text.Should().Contain("42");
        text.Should().Contain("5");
        text.Should().Contain("2026-08-18");
        text.Should().Contain("members");
        text.Should().Contain("generations");
    }

    [Fact]
    public void Arabic_and_english_captions_for_the_same_data_differ()
    {
        CaptionLocalizer.Format(Caption(CaptionLanguage.Ar))
            .Should().NotBe(CaptionLocalizer.Format(Caption(CaptionLanguage.En)));
    }

    [Fact]
    public void Defaulting_to_arabic_is_the_frontends_default_language()
    {
        // frontend/src/i18n: SUPPORTED_LANGUAGES = ['ar', 'en'], default 'ar'. Ar is not itself
        // a fallback value here (callers must pass a language), but this pins that Ar produces
        // Arabic labels, which is what the resolver falls back to.
        CaptionLocalizer.Format(Caption(CaptionLanguage.Ar)).Should().Contain("أفراد");
    }
}
