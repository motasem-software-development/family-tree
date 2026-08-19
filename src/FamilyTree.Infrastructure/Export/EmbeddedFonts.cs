using System.Reflection;
using SkiaSharp;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// The reference used Arial Bold and Open Sans Bold; neither ships (Arial is proprietary, Open
/// Sans has no Arabic coverage). Noto is SIL OFL and metrically close, so the reference's
/// column and row proportions survive (design §3.3).
///
/// Typefaces load once and are shared: the immutable font DATA is thread-safe to read, but
/// measured empirically (Round-2 review, byte-determinism test flaking under xUnit's parallel
/// test execution): concurrent <c>SKShaper.Shape</c>/<c>SKFont.MeasureText</c> calls against the
/// same shared <see cref="SKTypeface"/> from multiple threads can corrupt HarfBuzz's/Skia's own
/// internal shaping caches, producing different glyph runs for identical input text. Every
/// caller that shapes or measures text through this class's typefaces must hold
/// <see cref="ShapingLock"/> for the duration of that call -- see
/// <c>SkiaTreeRenderer.DrawShapedText</c> and <c>SkiaTextMeasurer.Measure</c>. Reloading per
/// request instead of locking would avoid the race but re-parse the font on every export.
/// </summary>
public static class EmbeddedFonts
{
    private static readonly Lazy<SKTypeface> ArabicFont =
        new(() => Load("NotoSansArabic-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<SKTypeface> LatinFont =
        new(() => Load("NotoSans-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Guards every shaping/measurement call against these typefaces -- see the class
    /// remarks. Not guarding font *selection* (<see cref="For"/>), only the HarfBuzz/Skia calls
    /// that walk the typeface's internal tables.</summary>
    public static readonly object ShapingLock = new();

    public static SKTypeface Arabic => ArabicFont.Value;
    public static SKTypeface Latin => LatinFont.Value;

    /// <summary>Arabic covers the names; Latin appears only in Latin captions.</summary>
    public static SKTypeface For(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Any(IsArabic) ? Arabic : Latin;
    }

    // U+0600–U+06FF Arabic, U+0750–U+077F Supplement, U+0870–U+089F Extended-B,
    // U+08A0–U+08FF Extended-A, U+FB50–U+FDFF Presentation Forms-A, and U+FE70–U+FEFC
    // Presentation Forms-B (stopping short of U+FEFD/U+FEFF, which are not Arabic letters —
    // U+FEFF in particular is the byte-order mark / zero-width no-break space).
    private static bool IsArabic(char c) =>
        c is >= '؀' and <= 'ۿ'
            or >= 'ݐ' and <= 'ݿ'
            or >= 'ࡰ' and <= '࢟'
            or >= 'ࢠ' and <= 'ࣿ'
            or >= 'ﭐ' and <= '﷿'
            or >= 'ﹰ' and <= 'ﻼ';

    private static SKTypeface Load(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded font '{fileName}' is missing. Check the EmbeddedResource item in the csproj.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"'{fileName}' is not a usable typeface.");
    }
}
