using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <param name="OffsetX">Scene-space origin of this page, used for tiling.</param>
public readonly record struct PageWindow(float Width, float Height, float OffsetX, float OffsetY);

/// <summary>One page, sized to the whole scene (design §4.5).</summary>
public static class SheetPaginator
{
    /// <param name="captionBandHeight">
    /// Extra device-point height appended below the scene for a bottom-margin caption (design
    /// §4.6). Zero (the default) reproduces the pre-caption page exactly. The band is genuinely
    /// empty canvas -- it extends the page beyond the scene's own bounds rather than drawing
    /// into the scene's margin, so no scale factor can make the tree collide with it.
    /// </param>
    public static IEnumerable<PageWindow> Pages(TreeScene scene, float captionBandHeight = 0f)
    {
        yield return new PageWindow(
            (float)(scene.Bounds.Width * scene.Scale),
            (float)(scene.Bounds.Height * scene.Scale) + captionBandHeight,
            0,
            0);
    }
}
