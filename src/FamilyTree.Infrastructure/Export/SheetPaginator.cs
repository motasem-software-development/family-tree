using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <param name="OffsetX">Scene-space origin of this page, used for tiling.</param>
/// <param name="ContentOffsetX">
/// Extra device-space horizontal shift applied to the scene's own translate, so the tree stays
/// centred when the page is wider than the scene needs (design §4.6, Important 2 fix). Zero
/// when the page is exactly the scene's width, which is the common case and what every existing
/// caller before that fix always got.
/// </param>
public readonly record struct PageWindow(
    float Width, float Height, float OffsetX, float OffsetY, float ContentOffsetX = 0f);

/// <summary>One page, sized to the whole scene (design §4.5).</summary>
public static class SheetPaginator
{
    /// <param name="captionBandHeight">
    /// Extra device-point height appended below the scene for a bottom-margin caption (design
    /// §4.6). Zero (the default) reproduces the pre-caption page exactly. The band is genuinely
    /// empty canvas -- it extends the page beyond the scene's own bounds rather than drawing
    /// into the scene's margin, so no scale factor can make the tree collide with it.
    /// </param>
    /// <param name="minWidth">
    /// The page is never narrower than this (design §4.6, Important 2 fix): a caption's own
    /// minimum width, even after shrinking its font and truncating the family tree name, can
    /// still exceed a small tree's natural page width -- shrinking and truncating the caption
    /// was found to not be enough on its own; the page has to be allowed to grow past the
    /// scene's own width instead of clipping the caption. Zero (the default) reproduces the
    /// pre-caption page exactly. The scene is centred in the extra width via
    /// <see cref="PageWindow.ContentOffsetX"/>.
    /// </param>
    public static IEnumerable<PageWindow> Pages(
        TreeScene scene, float captionBandHeight = 0f, float minWidth = 0f)
    {
        var sceneWidth = (float)(scene.Bounds.Width * scene.Scale);
        var pageWidth = MathF.Max(sceneWidth, minWidth);
        var contentOffsetX = (pageWidth - sceneWidth) / 2f;

        yield return new PageWindow(
            pageWidth,
            (float)(scene.Bounds.Height * scene.Scale) + captionBandHeight,
            0,
            0,
            contentOffsetX);
    }
}
