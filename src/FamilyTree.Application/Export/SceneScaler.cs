using FamilyTree.Domain.Common;

namespace FamilyTree.Application.Export;

/// <summary>
/// Design §4.4. The PDF format caps a page dimension at 14,400 units. Past that we scale the
/// whole scene uniformly rather than cropping it, and below the legibility floor we refuse
/// outright — an invalid or unreadable page is worse than an honest error.
/// </summary>
public static class SceneScaler
{
    /// <param name="reservedHeight">
    /// Device points reserved below the scene, on the FULL (unscaled) page, for a bottom-margin
    /// caption band (design §4.6). Must be subtracted from the height budget BEFORE computing
    /// the fit, not added to an already-maxed page afterward -- a scene scaled to land exactly
    /// at <see cref="LayoutMetrics.MaxPageExtent"/> and then given a band on top of that would
    /// push the page past the PDF format's legal maximum (Round-3 review, Critical 1: measured
    /// <c>/MediaBox [0 0 432 14428]</c>, 28pt over the 14400 cap, for a real reachable scene).
    /// Zero (the default) reproduces the pre-caption fit exactly.
    /// </param>
    public static TreeScene FitToSheet(TreeScene scene, LayoutMetrics metrics, double reservedHeight = 0)
    {
        var maxHeight = metrics.MaxPageExtent - reservedHeight;

        // Independent per-axis budgets, so a band that only ever applies to height cannot be
        // hidden inside a single "longest side" comparison: the axis NOT currently the longest
        // could still be close enough to the cap that reserving height pushes ITS scale below
        // 1.0 too, once the other axis stops being the binding constraint.
        var widthScale = scene.Bounds.Width > metrics.MaxPageExtent
            ? metrics.MaxPageExtent / scene.Bounds.Width
            : 1.0;
        var heightScale = scene.Bounds.Height > maxHeight
            ? maxHeight / scene.Bounds.Height
            : 1.0;

        var scale = Math.Min(widthScale, heightScale);
        if (scale >= 1.0) return scene with { Scale = 1.0 };

        if (metrics.BodyFontSize * scale < metrics.MinFontSize)
            throw new TooLargeException(
                "EXPORT_TREE_TOO_LARGE",
                "This tree cannot fit a single sheet legibly. Export it as A4 pages instead.",
                "sheet-overflow");

        return scene with { Scale = scale };
    }
}
