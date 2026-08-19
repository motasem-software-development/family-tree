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
/// <c>SkiaTreeRenderer.DrawShapedText</c> and <c>SkiaTextMeasurer.Measure</c>.
///
/// THROUGHPUT TRADE-OFF (Round-3 review, finding 4): this serialises every shaping/measurement
/// call app-wide, which the review measured as a real cost -- a 1,200-label Arabic tree got only
/// a 1.09x speedup from <c>FamilyTreeExportService</c>'s 2-way concurrency, against an ideal of
/// 2.00x, because shaping dominates render time and all of it is serialised. The preferred fix
/// (cache font BYTES here and give each render its own parsed <see cref="SKTypeface"/>, so
/// there is no shared typeface left to corrupt and no lock is needed at all) was not taken:
/// <c>SkiaTextMeasurer.Delegate</c> is a single static <c>MeasureText</c> the whole layout
/// pipeline shares (production call site: <c>TreeRendererAdapter</c>; test call sites:
/// <c>SkiaTreeRendererTests</c>, <c>SkiaTreeRendererCaptionTests</c>,
/// <c>SkiaExportRoundTripTests</c>, every layout-strategy test), and making it per-render would
/// mean threading a font-lifetime object through the layout strategies and every one of those
/// call sites -- a change with a blast radius well outside this task's caption scope, for a
/// throughput gain the production semaphore (2-way) only partly cashes in anyway. Reloading the
/// typeface per request without per-render isolation would avoid the race but re-parse the font
/// on every export for no concurrency benefit.
///
/// So the lock stays, narrowed instead to the smallest region that is actually touching shared
/// typeface state -- <c>SKShaper</c> construction, <c>Shape</c>, and per-glyph
/// <c>SKFont.MeasureText</c> -- which is what <c>SkiaTreeRenderer.DrawShapedText</c> does.
/// <c>DrawMarksAsPaths</c> and the final <c>canvas.DrawText</c> are canvas operations on
/// already-shaped data and sit outside it. Two paths still bypass the lock entirely and are a
/// known residual risk, not covered by this fix: <c>SkiaTextMeasurerTests</c>'s own raw
/// <c>SKFont(EmbeddedFonts.Arabic, …)</c> construction (test-only, single-threaded per test), and
/// the typeface being walked during PDF font embedding at <c>SKDocument.Close()</c> (production,
/// but has not been observed to corrupt the *rendered* output the way concurrent shaping did).
/// </summary>
public static class EmbeddedFonts
{
    private static readonly Lazy<SKTypeface> ArabicFont =
        new(() => Load("NotoSansArabic-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<SKTypeface> LatinFont =
        new(() => Load("NotoSans-Bold.ttf"), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Guards only the HarfBuzz/Skia calls that walk a shared typeface's internal
    /// tables during shaping or measurement -- see the class remarks for the narrowed scope and
    /// the throughput trade-off that scope accepts. Not guarding font *selection*
    /// (<see cref="For"/>), and not guarding canvas drawing of already-shaped data.</summary>
    public static readonly object ShapingLock = new();

    public static SKTypeface Arabic => ArabicFont.Value;
    public static SKTypeface Latin => LatinFont.Value;

    /// <summary>Arabic covers the names; Latin appears only in Latin captions. Picks by majority
    /// presence, not purity -- correct for a single-script run, and this is exactly why a caller
    /// with mixed-script text must split it into runs first (see <see cref="SplitByScript"/>)
    /// rather than calling this on the whole mixed string.</summary>
    public static SKTypeface For(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Any(IsArabic) ? Arabic : Latin;
    }

    /// <summary>
    /// Splits text into maximal runs that are entirely Arabic-script or entirely not, in
    /// original (encounter) order. A caption segment is a logical field, not a promise that its
    /// text is single-script -- the family tree name above all is free-form user input and may
    /// itself mix Arabic and Latin (Round-2 review, finding 3: shaping or font-selecting a
    /// segment as one buffer reproduces the original whole-caption defect inside a segment that
    /// looked "already fixed"). Every run this returns is safe to shape, measure, and
    /// font-select on its own via <see cref="For"/>.
    ///
    /// Whitespace does not by itself start a new run: it takes on whichever script surrounds it
    /// (the previous character's, or the next character's at the very start of the text), the
    /// same way plain Arabic text with an ordinary word-separating space is not "mixed script".
    /// Without this, splitting purely on <see cref="IsArabic"/> would cut an ordinary two-word
    /// Arabic name at its space (space is not itself in any Arabic range) into three runs and
    /// the caption's run layout -- which glues a segment's own runs together in encounter order,
    /// left to right, regardless of the caption's overall direction -- would then render the
    /// second word to the LEFT of the first, silently reversing plain Arabic text that was never
    /// mixed-script at all.
    /// </summary>
    public static IReadOnlyList<string> SplitByScript(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return [];

        var resolved = ResolveNeutralRuns(text);

        var runs = new List<string>();
        var start = 0;
        var runIsArabic = resolved[0];

        for (var i = 1; i < text.Length; i++)
        {
            if (resolved[i] == runIsArabic) continue;

            runs.Add(text[start..i]);
            start = i;
            runIsArabic = resolved[i];
        }

        runs.Add(text[start..]);
        return runs;
    }

    /// <summary>Per-character Arabic/not-Arabic classification with whitespace resolved to its
    /// neighbouring script rather than treated as its own category -- see
    /// <see cref="SplitByScript"/>.</summary>
    private static bool[] ResolveNeutralRuns(string text)
    {
        var isArabic = new bool?[text.Length];
        for (var i = 0; i < text.Length; i++)
            isArabic[i] = char.IsWhiteSpace(text[i]) ? null : IsArabic(text[i]);

        var resolved = new bool[text.Length];
        var last = false;
        var haveLast = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (isArabic[i] is { } value)
            {
                last = value;
                haveLast = true;
            }
            resolved[i] = haveLast ? last : NextResolved(isArabic, i);
        }

        return resolved;

        static bool NextResolved(bool?[] classes, int from)
        {
            for (var j = from; j < classes.Length; j++)
                if (classes[j] is { } value) return value;
            return false; // the whole string is whitespace -- classification is moot.
        }
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
