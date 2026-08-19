using System.Globalization;
using System.Linq;

namespace FamilyTree.Application.Export;

/// <summary>Distinguishes the family tree name from every other caption segment, so a renderer
/// that needs to shorten the caption to fit a page knows which segment it is allowed to
/// truncate (never a count or the date).</summary>
public enum CaptionSegmentKind { Name, Other }

/// <summary>
/// One logical field of the caption -- the family tree name, a separator, a count, a label, or
/// the date. A segment's <see cref="Text"/> is NOT assumed to be script-homogeneous: the family
/// tree name in particular is free-form user input and may itself mix Arabic and Latin. The
/// renderer is responsible for splitting each segment's text into script-homogeneous runs
/// before shaping (Round-2 review, finding 3) -- treating a segment as a shaping unit, rather
/// than assuming its text is single-script, is what let a mixed-script name reproduce the
/// original whole-string-shaping defect inside a single "already fixed" segment.
///
/// No padding space is baked into a segment's own text at its boundaries: the caption's
/// mixed-script layout puts each segment on whichever visual side its script and the caption's
/// reading direction dictate, and a space living inside one segment's text landed on only one
/// side of the join (Round-2 review, finding 1). Spacing between segments is the renderer's
/// job, not this class's.
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

    /// <summary>Plain-text rendering of the caption in logical (reading, not visual) order,
    /// segments joined with a single space. Nothing in production drawing uses this directly --
    /// the renderer lays out <see cref="Segments"/> itself, with its own measured inter-segment
    /// gap -- it exists for callers that just want the caption as one string (tests,
    /// diagnostics), built from the same segments so there is one source of truth for the
    /// caption's content.</summary>
    public static string Format(PdfCaption caption) =>
        string.Join(' ', Segments(caption).Select(s => s.Text));

    /// <summary>
    /// The caption broken into logical-field segments, in logical (reading) order for
    /// <see cref="PdfCaption.Language"/>. A renderer lays these out itself -- right-to-left for
    /// Ar, left-to-right for En -- splitting each segment into script-homogeneous runs, shaping
    /// and font-selecting each run on its own (so a segment being single-script or mixed makes
    /// no difference to correctness), and spacing adjacent segments with its own measured gap
    /// rather than a space character living inside either segment's text.
    /// </summary>
    public static IReadOnlyList<CaptionSegment> Segments(PdfCaption caption)
    {
        var date = caption.ExportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = new CaptionSegment(caption.FamilyTreeName, CaptionSegmentKind.Name);
        const string separator = "·";

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
            // unrecognised enum value falls back to. The count and its label are separate
            // segments -- not because either is script-mixed on its own, but so the renderer's
            // uniform inter-segment gap (not a baked-in space) is what separates them.
            _ =>
            [
                name,
                new CaptionSegment(separator),
                new CaptionSegment($"{caption.MemberCount}"),
                new CaptionSegment(MembersAr),
                new CaptionSegment(separator),
                new CaptionSegment($"{caption.GenerationCount}"),
                new CaptionSegment(GenerationsAr),
                new CaptionSegment(separator),
                new CaptionSegment(ExportedAr),
                new CaptionSegment(date)
            ]
        };
    }
}
