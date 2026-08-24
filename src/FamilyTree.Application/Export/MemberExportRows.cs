using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.Countries;
using FamilyTree.Contracts.FamilyMembers;

namespace FamilyTree.Application.Export;

/// <summary>
/// One row of the members workbook, in specification §19's column order.
///
/// Every identifier is a <see cref="string"/>, <see cref="Generation"/> alone is a number. The
/// row decides the <i>text</i>; the workbook decides the <i>cell type</i> — but a row that has
/// already turned "012345678" into a number cannot be rescued downstream, so the three
/// identifiers never leave this type as anything but text (design spec §7.3).
/// </summary>
public sealed record MemberExportRow(
    string NationalId,
    string FullName,
    string MobileNumber,
    string WhatsAppNumber,
    string Country,
    string Branch,
    int Generation,
    string Status);

/// <summary>
/// Turns the filtered member list into workbook rows — pure, and free of ClosedXML, so the part
/// most likely to be wrong is testable in milliseconds (design spec §7.1).
///
/// Localisation is a small lookup, following <see cref="CaptionLocalizer"/>: nothing else in this
/// codebase is localised server-side, and eight headers do not justify a framework.
/// </summary>
public static class MemberExportRows
{
    private static readonly string[] HeadersAr =
    [
        "رقم الهوية",
        "الاسم الكامل",
        "رقم الجوال",
        "رقم الواتساب",
        "بلد الإقامة",
        "الفرع",
        "الجيل",
        "الحالة"
    ];

    private static readonly string[] HeadersEn =
    [
        "National ID",
        "Full Name",
        "Mobile Number",
        "WhatsApp Number",
        "Country of Residence",
        "Branch",
        "Generation",
        "Status"
    ];

    private const string RootAr = "الجذر";
    private const string RootEn = "Root";
    private const string AliveAr = "على قيد الحياة";
    private const string AliveEn = "Alive";
    private const string DeceasedAr = "متوفى";
    private const string DeceasedEn = "Deceased";

    /// <summary>Specification §19's order. The workbook writes them left to right as given.</summary>
    public static IReadOnlyList<string> Headers(CaptionLanguage language) =>
        language is CaptionLanguage.Ar ? HeadersAr : HeadersEn;

    /// <param name="lineage">
    /// Every member of the family, not only the exported ones. Load-bearing: a filtered list has
    /// holes in it, and composing the name from it would drop a father the filter excluded — so
    /// a filtered export would carry different names than the same rows on screen. This mirrors
    /// what MembersPage does with its unfiltered query, for the same reason.
    /// </param>
    public static IReadOnlyList<MemberExportRow> Build(
        IReadOnlyList<FamilyMemberListItem> members,
        IReadOnlyDictionary<Guid, NamedMember> lineage,
        IReadOnlyList<CountryResponse> countries,
        CaptionLanguage language)
    {
        // Indexed once: every row with a country looks one up.
        var countryById = countries.ToDictionary(c => c.Id);

        return members.Select(member => new MemberExportRow(
            member.NationalId ?? string.Empty,
            MemberNameComposer.Compose(member.Id, lineage),
            member.MobileNumber ?? string.Empty,
            member.WhatsAppNumber ?? string.Empty,
            CountryOf(member.CountryId, countryById, language),
            // The root belongs to no branch; specification §21 renders that as "Root" rather than
            // as a blank cell.
            member.BranchName ?? Localised(language, RootAr, RootEn),
            member.Generation,
            member.IsDeceased
                ? Localised(language, DeceasedAr, DeceasedEn)
                : Localised(language, AliveAr, AliveEn)))
            .ToList();
    }

    /// <summary>
    /// The country's name in the export's language, or empty.
    ///
    /// Empty rather than throwing for an id the catalog does not hold: the member list and the
    /// country list are two responses, and they can disagree for one request. A missing country
    /// name is a blank cell, not a failed export.
    /// </summary>
    private static string CountryOf(
        int? countryId,
        IReadOnlyDictionary<int, CountryResponse> countryById,
        CaptionLanguage language)
    {
        if (countryId is not { } id) return string.Empty;
        if (!countryById.TryGetValue(id, out var country)) return string.Empty;

        return Localised(language, country.NameAr, country.NameEn);
    }

    private static string Localised(CaptionLanguage language, string arabic, string english) =>
        language is CaptionLanguage.Ar ? arabic : english;
}
