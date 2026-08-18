using FamilyTree.Domain.Common;

namespace FamilyTree.Application.Export;

/// <summary>
/// Design §4.4. The PDF format caps a page dimension at 14,400 units. Past that we scale the
/// whole scene uniformly rather than cropping it, and below the legibility floor we refuse
/// outright — an invalid or unreadable page is worse than an honest error.
/// </summary>
public static class SceneScaler
{
    public static TreeScene FitToSheet(TreeScene scene, LayoutMetrics metrics)
    {
        var longest = Math.Max(scene.Bounds.Width, scene.Bounds.Height);
        if (longest <= metrics.MaxPageExtent) return scene with { Scale = 1.0 };

        var scale = metrics.MaxPageExtent / longest;

        if (metrics.BodyFontSize * scale < metrics.MinFontSize)
            throw new TooLargeException(
                "EXPORT_TREE_TOO_LARGE",
                "This tree cannot fit a single sheet legibly. Export it as A4 pages instead.",
                "sheet-overflow");

        return scene with { Scale = scale };
    }
}
