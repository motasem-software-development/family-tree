using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <param name="OffsetX">Scene-space origin of this page, used for tiling.</param>
public readonly record struct PageWindow(float Width, float Height, float OffsetX, float OffsetY);

/// <summary>One page, sized to the whole scene (design §4.5).</summary>
public static class SheetPaginator
{
    public static IEnumerable<PageWindow> Pages(TreeScene scene)
    {
        yield return new PageWindow(
            (float)(scene.Bounds.Width * scene.Scale),
            (float)(scene.Bounds.Height * scene.Scale),
            0,
            0);
    }
}
