using System.Globalization;

namespace FamilyTree.Application.Export;

/// <summary>
/// Formats <see cref="PdfCaption"/> into the single restrained line drawn in the bottom margin
/// (design §4.6). This is the entire localisation surface for the caption -- a two-entry
/// lookup, not a framework: no <c>IStringLocalizer</c>, no <c>.resx</c>, because nothing else in
/// this codebase is localised server-side.
/// </summary>
public static class CaptionLocalizer
{
    private const string MembersAr = "أفراد";
    private const string GenerationsAr = "أجيال";
    private const string ExportedAr = "تاريخ التصدير";

    private const string MembersEn = "members";
    private const string GenerationsEn = "generations";
    private const string ExportedEn = "Exported";

    public static string Format(PdfCaption caption)
    {
        var date = caption.ExportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return caption.Language switch
        {
            CaptionLanguage.En => string.Join(" · ",
                caption.FamilyTreeName,
                $"{caption.MemberCount} {MembersEn}",
                $"{caption.GenerationCount} {GenerationsEn}",
                $"{ExportedEn} {date}"),

            // Ar is the default (frontend/src/i18n default 'ar'), so it is also what an
            // unrecognised enum value falls back to.
            _ => string.Join(" · ",
                caption.FamilyTreeName,
                $"{caption.MemberCount} {MembersAr}",
                $"{caption.GenerationCount} {GenerationsAr}",
                $"{ExportedAr} {date}")
        };
    }
}
