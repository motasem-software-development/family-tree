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
}
