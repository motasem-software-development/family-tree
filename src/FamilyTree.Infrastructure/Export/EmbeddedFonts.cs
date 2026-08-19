using System.Reflection;
using SkiaSharp;

namespace FamilyTree.Infrastructure.Export;

/// <summary>
/// The reference used Arial Bold and Open Sans Bold; neither ships (Arial is proprietary, Open
/// Sans has no Arabic coverage). Noto is SIL OFL and metrically close, so the reference's
/// column and row proportions survive (design §3.3).
///
/// Typefaces load once and are shared: they are immutable and thread-safe, and reloading per
/// request would re-parse the font on every export.
/// </summary>
public static class EmbeddedFonts
{
    private static readonly Lazy<SKTypeface> ArabicFont =
        new(() => Load("NotoSansArabic-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<SKTypeface> LatinFont =
        new(() => Load("NotoSans-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    public static SKTypeface Arabic => ArabicFont.Value;
    public static SKTypeface Latin => LatinFont.Value;

    /// <summary>Arabic covers the names; Latin appears only in Latin captions.</summary>
    public static SKTypeface For(string text) =>
        text.Any(IsArabic) ? Arabic : Latin;

    // U+0600–U+06FF Arabic, U+0750–U+077F Supplement, U+FB50–U+FDFF and U+FE70–U+FEFF forms.
    private static bool IsArabic(char c) =>
        c is >= '؀' and <= 'ۿ'
            or >= 'ݐ' and <= 'ݿ'
            or >= 'ﭐ' and <= '﷿'
            or >= 'ﹰ' and <= '﻿';

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
