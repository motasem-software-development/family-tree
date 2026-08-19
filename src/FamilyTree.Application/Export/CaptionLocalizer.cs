using System.Globalization;
using System.Linq;

namespace FamilyTree.Application.Export;

/// <summary>Distinguishes the family tree name from every other caption segment, so a renderer
/// that needs to shorten the caption to fit a page knows which segment it is allowed to
/// truncate (never a count or the date).</summary>
public enum CaptionSegmentKind { Name, Other }

/// <summary>
/// One homogeneous run of the caption -- either the family tree name, or a fixed piece of text
/// that is entirely Arabic or entirely Latin/digits/punctuation. Segments are homogeneous by
/// construction here, not by scanning the assembled string later: the caption is always
/// mixed-script (Arabic words, Latin digits, an ISO date), and shaping that as one buffer lets
/// HarfBuzz's single script/direction guess for the whole run reverse the embedded digits and
/// pick one font for glyphs the other script doesn't have. A renderer shapes, measures, and
/// lays out each segment independently instead (design §4.6).
/// </summary>
public sealed record CaptionSegment(string Text, CaptionSegmentKind Kind = CaptionSegmentKind.Other);

/// <summary>
/// Builds the bottom-margin caption (design §4.6). This is the entire localisation surface for
/// the caption -- a two-entry lookup, not a framework: no <c>IStringLocalizer</c>, no
/// <c>.resx</c>, because nothing else in this codebase is localised server-side.
/// </summary>
public static class CaptionLocalizer
{
    private const string MembersAr = "أفراد";
    private const string GenerationsAr = "أجيال";
    private const string ExportedAr = "تاريخ التصدير";

    private const string MembersEn = "members";
    private const string GenerationsEn = "generations";
    private const string ExportedEn = "Exported";

    /// <summary>Plain-text rendering of the caption in logical (reading, not visual) order.
    /// Nothing in production drawing uses this directly -- the renderer shapes
    /// <see cref="Segments"/> itself -- it exists for callers that just want the caption as one
    /// string (tests, diagnostics), built from the same segments so there is one source of
    /// truth for the caption's content.</summary>
    public static string Format(PdfCaption caption) =>
        string.Concat(Segments(caption).Select(s => s.Text));

    /// <summary>
    /// The caption broken into script-homogeneous segments, in logical (reading) order for
    /// <see cref="PdfCaption.Language"/>. A renderer lays these out itself -- right-to-left for
    /// Ar, left-to-right for En -- accumulating each segment's own measured width; because every
    /// segment is single-script, shaping it alone cannot reverse it, and picking a font per
    /// segment (rather than per whole caption) means Latin words never land on an Arabic-only
    /// typeface, or vice versa.
    /// </summary>
    public static IReadOnlyList<CaptionSegment> Segments(PdfCaption caption)
    {
        var date = caption.ExportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = new CaptionSegment(caption.FamilyTreeName, CaptionSegmentKind.Name);
        const string separator = " · ";

        return caption.Language switch
        {
            CaptionLanguage.En =>
            [
                name,
                new CaptionSegment(separator),
                new CaptionSegment($"{caption.MemberCount} {MembersEn}"),
                new CaptionSegment(separator),
                new CaptionSegment($"{caption.GenerationCount} {GenerationsEn}"),
                new CaptionSegment(separator),
                new CaptionSegment($"{ExportedEn} {date}")
            ],

            // Ar is the default (frontend/src/i18n default 'ar'), so it is also what an
            // unrecognised enum value falls back to. The count and the date are their own
            // segments, split away from the surrounding Arabic label -- "42 أفراد" is not
            // homogeneous, but "42 " and "أفراد" each are.
            _ =>
            [
                name,
                new CaptionSegment(separator),
                new CaptionSegment($"{caption.MemberCount} "),
                new CaptionSegment(MembersAr),
                new CaptionSegment(separator),
                new CaptionSegment($"{caption.GenerationCount} "),
                new CaptionSegment(GenerationsAr),
                new CaptionSegment(separator),
                new CaptionSegment($"{ExportedAr} "),
                new CaptionSegment(date)
            ]
        };
    }
}
