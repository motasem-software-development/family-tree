using FamilyTree.Application.Export;
using FamilyTree.Domain.Common;
using FamilyTree.Infrastructure.Export;
using FluentAssertions;
using SkiaSharp;

namespace FamilyTree.Application.Tests.Export;

/// <summary>
/// Per-page culling for <c>page=a4</c> (final review, Critical 2). The renderer redrew every node
/// and connector on every tile, so cost grew as pages x content: 10,000 members took 243.17 s
/// across 182 A4 pages against 0.87 s for the same tree on one sheet, all of it serialised behind
/// the process-wide shaping lock and holding one of two render slots.
///
/// <para>
/// The defect is invisible in the OUTPUT -- Skia's own clip already discards the off-page
/// drawing, so an uncullled render produces a nearly identical PDF -- which is why no
/// output-based test ever caught it. What the culling changes is how much shaping happens before
/// that clip, so these tests assert on the decision itself: the geometry of the visibility test,
/// and the total number of items a full tiling would draw.
/// </para>
/// </summary>
public sealed class SceneCullingTests
{
    private static SceneNode Node(double x, double y) =>
        new(Guid.NewGuid(), "سالم", x, y, 60, 20, 13.34, "#000000", NodeShape.RoundedBox);

    private static SKRect Small(double x, double y) => new((float)x, (float)y, (float)x + 1, (float)y + 1);

    [Fact]
    public void An_item_inside_the_page_window_is_visible()
    {
        var page = new PageWindow(595, 842, 0, 0);

        SceneCulling.IsVisible(Small(100, 100), 1.0, page).Should().BeTrue();
    }

    [Fact]
    public void An_item_beyond_the_page_window_is_culled()
    {
        var page = new PageWindow(595, 842, 0, 0);

        SceneCulling.IsVisible(Small(5_000, 100), 1.0, page).Should().BeFalse();
        SceneCulling.IsVisible(Small(100, 5_000), 1.0, page).Should().BeFalse();
    }

    /// <summary>
    /// The second tile's window starts at a scene offset, so an item at the far left of the scene
    /// is off IT even though it was on the first tile -- and an item at the offset is on.
    /// </summary>
    [Fact]
    public void A_tile_is_culled_against_its_own_offset_not_against_the_scene_origin()
    {
        var secondTile = new PageWindow(595, 842, 577, 0);

        SceneCulling.IsVisible(Small(10, 10), 1.0, secondTile).Should().BeFalse();
        SceneCulling.IsVisible(Small(700, 10), 1.0, secondTile).Should().BeTrue();
    }

    /// <summary>
    /// Offsets and page sizes are device-space; scene coordinates are not. Scaling must be
    /// applied to the scene side only -- scaling the offsets too would shift every window and
    /// cull the wrong band of a reduced scene.
    /// </summary>
    [Fact]
    public void The_visibility_test_applies_the_scene_scale_to_scene_coordinates_only()
    {
        var page = new PageWindow(595, 842, 0, 0);

        SceneCulling.IsVisible(Small(2_000, 10), 1.0, page).Should().BeFalse();
        SceneCulling.IsVisible(Small(2_000, 10), 0.25, page).Should().BeTrue();
    }

    /// <summary>
    /// A sheet grown wider than its scene to fit a caption shifts the whole scene right by
    /// <see cref="PageWindow.ContentOffsetX"/>. Dropping that term from the transform would cull
    /// against a window displaced by exactly that shift.
    /// </summary>
    [Fact]
    public void The_visibility_test_honours_a_grown_pages_content_offset()
    {
        var grown = new PageWindow(400, 200, 0, 0, ContentOffsetX: 300);

        // Scene x = 380 sits on a 400-wide page when the scene starts at the page's own left
        // edge, and is pushed off it once the scene is shifted 300 points right to centre it.
        SceneCulling.IsVisible(Small(380, 10), 1.0, grown with { ContentOffsetX = 0 }).Should().BeTrue();
        SceneCulling.IsVisible(Small(380, 10), 1.0, grown).Should().BeFalse();
    }

    /// <summary>
    /// Node bounds must cover the LABEL, not just the box: a tick-shaped node has no box at all
    /// and its <see cref="SceneNode.Height"/> understates the text that hangs above and below the
    /// centre line. A node whose box is just off the page but whose label reaches onto it must
    /// still be drawn.
    /// </summary>
    [Fact]
    public void Node_bounds_extend_past_the_box_to_cover_the_label()
    {
        var node = Node(10, 10);
        var bounds = SceneCulling.BoundsOf(node);

        bounds.Left.Should().BeLessThan((float)node.X);
        bounds.Right.Should().BeGreaterThan((float)(node.X + node.Width));
        bounds.Top.Should().BeLessThan((float)(node.Y - node.Height / 2));
        bounds.Bottom.Should().BeGreaterThan((float)(node.Y + node.Height / 2));
    }

    /// <summary>
    /// The defect itself, stated as a number. Tiling a scene many times its own page size used to
    /// draw every item on every tile; with culling the TOTAL across all tiles stays close to the
    /// item count, the excess being only items that genuinely straddle a cut (pages overlap by a
    /// deliberate bleed, so a straddling item belongs on both tiles).
    /// </summary>
    [Fact]
    public void Tiling_a_large_scene_draws_each_item_a_bounded_number_of_times_in_total()
    {
        // A 20 x 20 grid spread over roughly 5,000 x 5,000 points: far apart enough that most
        // nodes sit wholly inside one tile.
        var nodes = Enumerable.Range(0, 20)
            .SelectMany(row => Enumerable.Range(0, 20).Select(col => Node(col * 250, row * 250)))
            .ToList();

        var scene = new TreeScene(nodes, [], new SceneBounds(0, 0, 5_000, 5_000));
        var pages = A4Paginator.Pages(scene).ToList();

        var bounds = nodes.Select(SceneCulling.BoundsOf).ToList();
        var drawn = pages.Sum(page => bounds.Count(b => SceneCulling.IsVisible(b, scene.Scale, page)));

        pages.Count.Should().BeGreaterThan(40, "the scene must be many tiles wide and tall");
        drawn.Should().BeLessThan(
            nodes.Count * 2,
            "without culling this is pages x nodes = {0}", pages.Count * nodes.Count);
    }

    /// <summary>
    /// Every node must still land on at least one tile: an over-aggressive visibility test would
    /// make the bound above look better while silently dropping content from a printed poster.
    /// </summary>
    [Fact]
    public void Tiling_leaves_no_item_undrawn()
    {
        var nodes = Enumerable.Range(0, 20)
            .SelectMany(row => Enumerable.Range(0, 20).Select(col => Node(col * 250, row * 250)))
            .ToList();

        var scene = new TreeScene(nodes, [], new SceneBounds(0, 0, 5_000, 5_000));
        var pages = A4Paginator.Pages(scene).ToList();

        nodes.Select(SceneCulling.BoundsOf)
            .Where(b => !pages.Any(page => SceneCulling.IsVisible(b, scene.Scale, page)))
            .Should().BeEmpty();
    }
}

/// <summary>
/// The second half of the Critical 2 fix: culling is the cost fix, but the page count itself must
/// also be bounded, so <c>page=a4</c> cannot be an unbounded lever even if culling regresses --
/// especially since the sheet path's own 413 message directs callers to it.
/// </summary>
public sealed class A4PageCapTests
{
    private static TreeScene SceneSpanning(double extent) =>
        new(
            [new SceneNode(Guid.NewGuid(), "سالم", 0, 0, 60, 20, 13.34, "#000000", NodeShape.RoundedBox)],
            [],
            new SceneBounds(0, 0, extent, extent));

    /// <summary>
    /// A scene's BOUNDS drive tiling, and nothing in the member cap bounds an aspect ratio: a
    /// scene can tile into thousands of nearly-empty pages without ever approaching 10,000
    /// members, which is exactly the case the member cap cannot see.
    /// </summary>
    [Fact]
    public void An_a4_export_past_the_page_cap_is_refused_with_its_own_reason()
    {
        var act = () => new SkiaTreeRenderer().Render(SceneSpanning(40_000), ExportPageFormat.A4);

        act.Should().Throw<TooLargeException>()
            .Where(e => e.Code == "EXPORT_TREE_TOO_LARGE" && e.Reason == "a4-page-cap");
    }

    /// <summary>The cap is a ceiling, not a general refusal: an ordinary A4 export still renders.</summary>
    [Fact]
    public void An_a4_export_within_the_page_cap_still_renders()
    {
        var act = () => new SkiaTreeRenderer().Render(SceneSpanning(3_000), ExportPageFormat.A4);

        act.Should().NotThrow();
    }

    /// <summary>
    /// The sheet format is one page by construction, so the cap must not reach it -- a sheet
    /// scene of the same span is refused (or scaled) by <c>SceneScaler</c>, on its own terms.
    /// </summary>
    [Fact]
    public void The_page_cap_does_not_apply_to_a_single_sheet()
    {
        var act = () => new SkiaTreeRenderer().Render(SceneSpanning(12_000), ExportPageFormat.Sheet);

        act.Should().NotThrow<TooLargeException>();
    }

    /// <summary>
    /// A cancelled request must stop the render, not merely stop the caller waiting for it: the
    /// token used to be observed only at the render semaphore, so a client disconnect left the
    /// CPU burning inside one of two process-wide slots until the document finished.
    /// </summary>
    [Fact]
    public void A_cancelled_export_stops_before_drawing_pages()
    {
        var act = () => new SkiaTreeRenderer()
            .Render(SceneSpanning(3_000), ExportPageFormat.A4, null, new CancellationToken(true));

        act.Should().Throw<OperationCanceledException>();
    }
}
