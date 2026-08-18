using FamilyTree.Application.Export;
using FamilyTree.Domain.Common;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class SceneScalerTests
{
    private static readonly LayoutMetrics Metrics = new();

    private static TreeScene SceneOfHeight(double height) =>
        new(
            [new SceneNode(Guid.NewGuid(), "x", 0, height / 2, 10, 10, 13.34, "#000000", NodeShape.Tick)],
            [],
            new SceneBounds(0, 0, 100, height));

    [Fact]
    public void A_scene_inside_the_ceiling_is_returned_unscaled()
    {
        SceneScaler.FitToSheet(SceneOfHeight(3642), Metrics).Scale.Should().Be(1.0);
    }

    [Fact]
    public void A_scene_past_the_ceiling_is_scaled_to_fit_exactly()
    {
        var fitted = SceneScaler.FitToSheet(SceneOfHeight(Metrics.MaxPageExtent * 2), Metrics);

        fitted.Scale.Should().BeApproximately(0.5, 1e-9);
        (fitted.Bounds.Height * fitted.Scale).Should()
            .BeLessThanOrEqualTo(Metrics.MaxPageExtent + 1e-6);
    }

    // Design §4.4: emitting an illegible page is the one outcome explicitly ruled out.
    [Fact]
    public void A_scene_needing_a_font_below_the_floor_is_refused()
    {
        // Body text is 13.34pt and the floor is 6pt, so any scale under ~0.45 must refuse.
        var act = () => SceneScaler.FitToSheet(SceneOfHeight(Metrics.MaxPageExtent * 10), Metrics);

        act.Should().Throw<TooLargeException>()
            .Where(e => e.Code == "EXPORT_TREE_TOO_LARGE" && e.Reason == "sheet-overflow");
    }

    [Fact]
    public void Width_overflow_is_caught_as_well_as_height()
    {
        var scene = new TreeScene([], [], new SceneBounds(0, 0, Metrics.MaxPageExtent * 1.5, 100));

        SceneScaler.FitToSheet(scene, Metrics).Scale.Should().BeApproximately(1 / 1.5, 1e-9);
    }
}
