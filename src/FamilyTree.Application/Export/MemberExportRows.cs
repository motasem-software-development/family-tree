using FamilyTree.Application.FamilyMembers;
using FamilyTree.Application.Reports;
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
///
/// The two dates and <see cref="Age"/> are the exception to "every identifier is a string": they
/// are quantities, and a workbook that cannot sort by birth date or filter by age is a workbook
/// that has thrown the answer away. Each is nullable, and null means "not recorded" — never a
/// zero and never the word "null".
/// </summary>
public sealed record MemberExportRow(
    string NationalId,
    string FullName,
    string MobileNumber,
    string WhatsAppNumber,
    string Country,
    string Branch,
    int Generation,
    string Status,
    DateOnly? DateOfBirth,
    int? Age,
    DateOnly? DateOfDeath);

/// <summary>
/// Turns the filtered member list into workbook rows — pure, and free of ClosedXML, so the part
/// most likely to be wrong is testable in milliseconds (design spec §7.1).
///
/// Localisation is a small lookup, following <see cref="CaptionLocalizer"/>: nothing else in this
/// codebase is localised server-side, and eleven headers do not justify a framework.
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
        "الحالة",
        "تاريخ الميلاد",
        "العمر",
        "تاريخ الوفاة"
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
        "Status",
        "Date of Birth",
        "Age",
        "Date of Death"
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
    /// <param name="today">
    /// The reference day a living member's age is measured against, in UTC — the same day the
    /// domain bounds a birth date by. Passed in rather than read from the clock here so the age
    /// column is testable without freezing time globally.
    /// </param>
    public static IReadOnlyList<MemberExportRow> Build(
        IReadOnlyList<FamilyMemberListItem> members,
        IReadOnlyDictionary<Guid, NamedMember> lineage,
        IReadOnlyList<CountryResponse> countries,
        CaptionLanguage language,
        DateOnly today)
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
                : Localised(language, AliveAr, AliveEn),
            member.DateOfBirth,
            AgeOf(member, today),
            member.DateOfDeath))
            .ToList();
    }

    /// <summary>
    /// Whole years lived: to the death date where there is one, to <paramref name="today"/>
    /// otherwise — the same split <see cref="LifeStatusCalculator"/> makes between a living
    /// member's age and a deceased one's lifespan, and the only reading of "age" that stays true
    /// as the file is opened again next year.
    ///
    /// Null, meaning a blank cell, in the two cases where no honest number exists: no birth date
    /// at all, and a member marked deceased whose death date was never recorded — measuring that
    /// one against today would quietly report an age they never reached.
    /// </summary>
    private static int? AgeOf(FamilyMemberListItem member, DateOnly today)
    {
        if (member.DateOfBirth is not { } born) return null;
        if (member.DateOfDeath is { } died) return Ages.YearsBetween(born, died);

        return member.IsDeceased ? null : Ages.YearsBetween(born, today);
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
