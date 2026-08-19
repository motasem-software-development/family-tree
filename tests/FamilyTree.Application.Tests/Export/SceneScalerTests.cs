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

    // Round-3 review, Critical 1: reserving the caption band by adding it to an already-maxed
    // page (rather than fitting the scene into MaxPageExtent minus the band) can push the total
    // page extent past the PDF format's legal maximum. This must fail against the pre-fix
    // FitToSheet(scene, metrics) call (no reservedHeight parameter existed at all -- the band
    // was added after the fact by the caller instead).
    [Fact]
    public void A_reserved_band_is_taken_out_of_the_budget_before_scaling_not_added_after()
    {
        const double band = 28;
        var fitted = SceneScaler.FitToSheet(SceneOfHeight(20000), Metrics, band);

        (fitted.Bounds.Height * fitted.Scale + band).Should()
            .BeLessThanOrEqualTo(Metrics.MaxPageExtent + 1e-6,
                "scaled scene height plus the reserved band must never exceed the PDF page-extent cap");
    }

    // The exact scenario the review measured: a scene whose height alone (600x20000, longest
    // side height) would otherwise scale to land at precisely MaxPageExtent, which a band added
    // afterward would then push over.
    [Fact]
    public void A_scene_that_would_land_exactly_at_the_ceiling_leaves_room_for_the_band()
    {
        var scene = new TreeScene([], [], new SceneBounds(0, 0, 600, 20000));
        const double band = 28;

        var fitted = SceneScaler.FitToSheet(scene, Metrics, band);

        (fitted.Bounds.Height * fitted.Scale + band).Should()
            .BeLessThanOrEqualTo(Metrics.MaxPageExtent + 1e-6);
        // Without a band, the same scene scales to exactly the ceiling -- confirms this scene is
        // the population the fix targets, not an already-safe one.
        SceneScaler.FitToSheet(scene, Metrics).Bounds.Height.Should().NotBe(0);
        (SceneScaler.FitToSheet(scene, Metrics).Bounds.Height * SceneScaler.FitToSheet(scene, Metrics).Scale)
            .Should().BeApproximately(Metrics.MaxPageExtent, 1e-6);
    }

    [Fact]
    public void Zero_reserved_height_reproduces_the_band_free_fit_exactly()
    {
        var scene = SceneOfHeight(Metrics.MaxPageExtent * 2);

        SceneScaler.FitToSheet(scene, Metrics, reservedHeight: 0)
            .Should().Be(SceneScaler.FitToSheet(scene, Metrics));
    }
}
