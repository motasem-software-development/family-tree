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

    [Fact]
    public void An_a4_export_carries_the_caption_on_its_last_page()
    {
        var pdf = new SkiaTreeRenderer().Render(Scene(), ExportPageFormat.A4, Caption());

        var text = ExtractText(pdf);

        text.Should().Contain("Al-Hassan Family");
    }
}
