using FamilyTree.Application.Export;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;

namespace FamilyTree.Application.Tests.Export;

public sealed class A4PaginatorTests
{
    private static TreeScene Scene(double width, double height) =>
        new([], [], new SceneBounds(0, 0, width, height));

    private static TreeScene Scene(double width, double height, double scale) =>
        new([], [], new SceneBounds(0, 0, width, height), scale);

    [Fact]
    public void A_scene_smaller_than_one_page_produces_one_page()
    {
        A4Paginator.Pages(Scene(400, 600)).Should().ContainSingle();
    }

    [Fact]
    public void A_tall_scene_is_tiled_down_the_page()
    {
        var pages = A4Paginator.Pages(Scene(400, 2400)).ToList();

        pages.Count.Should().BeGreaterThan(2);
        pages.Should().OnlyContain(p => p.Width <= 595 && p.Height <= 842);
    }

    [Fact]
    public void A_wide_and_tall_scene_is_tiled_in_both_directions()
    {
        var pages = A4Paginator.Pages(Scene(1400, 2000)).ToList();

        pages.Select(p => p.OffsetX).Distinct().Count().Should().BeGreaterThan(1);
        pages.Select(p => p.OffsetY).Distinct().Count().Should().BeGreaterThan(1);
    }

    // A connector crossing a cut must appear on both sheets, or the printed poster cannot be
    // reassembled (design §4.5).
    [Fact]
    public void Consecutive_rows_overlap_by_the_bleed()
    {
        var pages = A4Paginator.Pages(Scene(400, 2400)).ToList();

        var first = pages[0];
        var second = pages[1];

        (first.OffsetY + first.Height - second.OffsetY).Should().BeApproximately(18f, 1e-4f);
    }

    [Fact]
    public void Consecutive_columns_overlap_by_the_bleed()
    {
        var pages = A4Paginator.Pages(Scene(1600, 400)).ToList();

        var first = pages[0];
        var second = pages[1];

        second.OffsetY.Should().Be(first.OffsetY, "the two pages compared must be side by side");
        (first.OffsetX + first.Width - second.OffsetX).Should().BeApproximately(18f, 1e-4f);
    }

    [Fact]
    public void Every_part_of_the_scene_is_covered_by_some_page()
    {
        var pages = A4Paginator.Pages(Scene(1400, 2000)).ToList();

        pages.Max(p => p.OffsetX + p.Width).Should().BeGreaterThanOrEqualTo(1400);
        pages.Max(p => p.OffsetY + p.Height).Should().BeGreaterThanOrEqualTo(2000);
    }

    [Fact]
    public void A_scene_exactly_a_multiple_of_the_usable_tile_size_has_no_trailing_blank_page()
    {
        const float StepX = 595f - 18f;
        const float StepY = 842f - 18f;

        // Width/height chosen so the scaled extent lands exactly on N usable-tile boundaries:
        // last tile's far edge equals the scene extent exactly, with no remainder to spill a
        // further row/column.
        var width = 595f + StepX; // two columns exactly
        var height = 842f + (2 * StepY); // three rows exactly

        var pages = A4Paginator.Pages(Scene(width, height)).ToList();

        pages.Select(p => p.OffsetX).Distinct().Should().HaveCount(2);
        pages.Select(p => p.OffsetY).Distinct().Should().HaveCount(3);
        pages.Should().HaveCount(6);
    }

    // OffsetX/OffsetY/Width/Height are device (already-scaled) points: SkiaTreeRenderer
    // translates by the offset BEFORE scaling. A paginator that tiled over raw scene units
    // instead of scene.Bounds * scene.Scale would silently misplace every page after the first.
    [Fact]
    public void Tiling_is_computed_over_the_scaled_extent_not_raw_scene_units()
    {
        var scaledPages = A4Paginator.Pages(Scene(1400, 2000)).ToList();
        var rawUnitsAtHalfScale = A4Paginator.Pages(Scene(2800, 4000, 0.5)).ToList();

        rawUnitsAtHalfScale.Should().BeEquivalentTo(scaledPages);
    }

    // Round-2 review, finding 6: captionBandHeight > 0 was never exercised at the paginator
    // level, and reusing the existing (band-free) formulas here would be wrong -- they compute
    // from p.Height (842), which is the physical page, not the content window (842 - band) once
    // a band is reserved. These tests compute from the content window explicitly instead.
    private const float Band = 28f; // matches SkiaTreeRenderer.CaptionBandHeight

    [Fact]
    public void A_reserved_band_does_not_change_the_physical_page_size()
    {
        A4Paginator.Pages(Scene(400, 2400), Band)
            .Should().OnlyContain(p => p.Width == 595f && p.Height == 842f,
                "the physical sheet is always full A4 -- the band is a content-window concept, not a page-size one");
    }

    [Fact]
    public void Consecutive_rows_overlap_by_the_bleed_with_a_caption_band_reserved()
    {
        var pages = A4Paginator.Pages(Scene(400, 2400), Band).ToList();
        pages.Count.Should().BeGreaterThan(1, "the scene must actually tile for this to be meaningful");

        var contentHeight = 842f - Band;
        var first = pages[0];
        var second = pages[1];

        (first.OffsetY + contentHeight - second.OffsetY).Should().BeApproximately(18f, 1e-4f);
    }

    [Fact]
    public void Every_part_of_the_scene_is_covered_by_some_page_with_a_caption_band_reserved()
    {
        // Round-3 review, finding 5: height 2000 does not discriminate here -- mutating the row
        // loop-exit back to the band-free `y + PageHeight >= height` (842 instead of the 814
        // content window) still leaves this scene fully covered, so the test stayed green
        // against exactly the bug finding 6 (round 2) named. Height 2422 is sensitive to it: the
        // correct (814-window) tiling reaches y=2388 (covering to 3202), while the mutant stops
        // at y=1592 (covering only to 2434... short of a scene that needs it) -- concretely, the
        // mutant's last row starts at 1592 and the 842-tall mutant window reaches 2434, so bump
        // to a height the mutant provably cannot reach: verified below against the ACTUAL
        // (814-window) row count, not just an inequality a coincidentally-generous mutant could
        // still satisfy.
        var pages = A4Paginator.Pages(Scene(1400, 2422), Band).ToList();
        var contentHeight = 842f - Band;

        pages.Max(p => p.OffsetX + p.Width).Should().BeGreaterThanOrEqualTo(1400);
        pages.Max(p => p.OffsetY + contentHeight).Should().BeGreaterThanOrEqualTo(2422);

        // Directly pins the row count the 814-window tiling produces for this scene, so a mutant
        // that silently reverts to stepping by the 842-tall physical page (fewer, wider-spaced
        // rows) changes the row count and fails here even if the max-coverage inequality above
        // happened not to catch it.
        pages.Select(p => p.OffsetY).Distinct().Should().HaveCount(4);
    }

    // The band only shrinks the vertical content window; column (X) tiling is untouched.
    [Fact]
    public void A_caption_band_does_not_change_column_stepping()
    {
        var withBand = A4Paginator.Pages(Scene(1600, 400), Band).ToList();
        var withoutBand = A4Paginator.Pages(Scene(1600, 400)).ToList();

        withBand.Select(p => p.OffsetX).Should().BeEquivalentTo(withoutBand.Select(p => p.OffsetX));
    }

    // A reserved band means less usable content window per row, so a scene that fit in N
    // band-free rows needs at least as many rows once a band is reserved.
    [Fact]
    public void A_reserved_band_produces_at_least_as_many_rows_as_no_band()
    {
        var withoutBand = A4Paginator.Pages(Scene(400, 2400)).Select(p => p.OffsetY).Distinct().Count();
        var withBand = A4Paginator.Pages(Scene(400, 2400), Band).Select(p => p.OffsetY).Distinct().Count();

        withBand.Should().BeGreaterThanOrEqualTo(withoutBand);
    }

    [Fact]
    public void Zero_band_height_reproduces_the_band_free_tiling_exactly()
    {
        A4Paginator.Pages(Scene(1400, 2000), captionBandHeight: 0f)
            .Should().BeEquivalentTo(A4Paginator.Pages(Scene(1400, 2000)));
    }
}
