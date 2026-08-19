using FamilyTree.Application.Export;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Tiles one scene across A4 pages (design §4.5). Pages overlap by <see cref="Bleed"/> so a
/// connector crossing a cut appears on both sheets — without it the printed poster cannot be
/// reassembled, because the reader cannot tell which line continues where.
/// </summary>
public static class A4Paginator
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Bleed = 18f;

    public static IEnumerable<PageWindow> Pages(TreeScene scene)
    {
        var width = (float)(scene.Bounds.Width * scene.Scale);
        var height = (float)(scene.Bounds.Height * scene.Scale);

        // Each step advances by less than a full page, so successive windows overlap by Bleed.
        var stepX = PageWidth - Bleed;
        var stepY = PageHeight - Bleed;

        for (var y = 0f; ; y += stepY)
        {
            for (var x = 0f; ; x += stepX)
            {
                yield return new PageWindow(PageWidth, PageHeight, x, y);
                if (x + PageWidth >= width) break;
            }

            if (y + PageHeight >= height) break;
        }
    }
}
