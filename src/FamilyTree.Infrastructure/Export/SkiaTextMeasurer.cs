using FamilyTree.Application.Export;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// Supplies the <see cref="MeasureText"/> delegate the layout engine consumes (design §4.2).
/// Widths must come from *shaped* text: Arabic is cursive, so joined forms are narrower than
/// the isolated glyphs, and measuring unshaped would size every column too wide.
/// </summary>
public static class SkiaTextMeasurer
{
    public static MeasureText Delegate { get; } = Measure;

    /// <summary>
    /// The width the layout engine sizes a column from. A mixed-script string is measured the
    /// same way the caption has always measured a mixed-script segment
    /// (<c>SkiaTreeRenderer.BuildSegmentLayouts</c>): each script run measured on its own, with
    /// one measured word gap between adjacent runs. Measuring such a string as a single buffer
    /// instead resolves the minority script's characters against a typeface with no coverage for
    /// them, and sums .notdef advances -- which are uniform and unrelated to the real glyphs, so
    /// the column comes out wrong as well as printing as boxes (final review, Critical 1).
    ///
    /// <para>
    /// Single-script text (the overwhelming majority, and every string this codebase measured
    /// before mixed-script handling existed) still goes through the single-buffer path
    /// unchanged -- see <see cref="EmbeddedFonts.IsMixedScript"/> for why that gate is
    /// load-bearing rather than an optimisation, and note that it is also what keeps
    /// <c>Measure(" ")</c> -- the gap this very method computes -- from recursing into a split
    /// that would drop the whitespace and return zero.
    /// </para>
    /// </summary>
    public static double Measure(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (!EmbeddedFonts.IsMixedScript(text)) return MeasureRun(text, fontSize);

        var runs = EmbeddedFonts.SplitByScript(text);
        var gap = MeasureRun(" ", fontSize);

        return runs.Sum(run => MeasureRun(run, fontSize)) + gap * Math.Max(0, runs.Count - 1);
    }

    /// <summary>Measures one script-homogeneous run as a single shaped buffer.</summary>
    private static double MeasureRun(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var typeface = EmbeddedFonts.For(text);

        // See EmbeddedFonts.ShapingLock: concurrent shaping against a shared typeface can
        // corrupt HarfBuzz's/Skia's own caches.
        lock (EmbeddedFonts.ShapingLock)
        {
            using var font = new SKFont(typeface, (float)fontSize);
            using var shaper = new SKShaper(typeface);

            return shaper.Shape(text, font).Width;
        }
    }
}
