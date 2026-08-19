using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Tiles one scene across A4 pages (design §4.5). Pages overlap by <see cref="Bleed"/> so a
/// connector crossing a cut appears on both sheets — without it the printed poster cannot be
/// reassembled, because the reader cannot tell which line continues where.
/// </summary>
public static class A4Paginator
{
    internal const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Bleed = 18f;

    /// <param name="captionBandHeight">
    /// Device-point height reserved at the bottom of EVERY tile for a caption (design §4.6).
    /// Zero (the default) reproduces the pre-caption tiling exactly. The physical page stays a
    /// full 595×842 sheet -- the renderer clips scene content out of the bottom
    /// <paramref name="captionBandHeight"/> of each tile -- but the vertical *content* window
    /// each tile pulls from the scene shrinks to <c>PageHeight - captionBandHeight</c>, and rows
    /// step by that reduced height (still overlapping by <see cref="Bleed"/>): whatever a tile's
    /// clip cuts off its bottom, the next tile's top picks back up, so nothing is lost.
    /// </param>
    public static IEnumerable<PageWindow> Pages(TreeScene scene, float captionBandHeight = 0f)
    {
        var width = (float)(scene.Bounds.Width * scene.Scale);
        var height = (float)(scene.Bounds.Height * scene.Scale);

        var contentHeight = PageHeight - captionBandHeight;

        // Each step advances by less than a full content window, so successive windows overlap
        // by Bleed.
        var stepX = PageWidth - Bleed;
        var stepY = contentHeight - Bleed;

        for (var y = 0f; ; y += stepY)
        {
            for (var x = 0f; ; x += stepX)
            {
                yield return new PageWindow(PageWidth, PageHeight, x, y);
                if (x + PageWidth >= width) break;
            }

            if (y + contentHeight >= height) break;
        }
    }
}
