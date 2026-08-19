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

    public static double Measure(string text, double fontSize)
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
