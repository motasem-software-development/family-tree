namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// The seeded country list. Not exhaustive by design: Palestine, the Arab world, and the main
/// destinations of the Palestinian diaspora cover where this family actually lives. Adding a
/// country later is one entry here plus a re-run of the seeder, which is idempotent by code.
///
/// Note that DialCode is NOT unique — US and CA both use +1. Only Code is.
/// </summary>
public static class CountryCatalog
{
    public static IReadOnlyList<(string Code, string NameAr, string NameEn, string DialCode)> All { get; } =
    [
        ("PS", "فلسطين", "Palestine", "+970"),
        ("JO", "الأردن", "Jordan", "+962"),
        ("EG", "مصر", "Egypt", "+20"),
        ("SA", "السعودية", "Saudi Arabia", "+966"),
        ("AE", "الإمارات", "United Arab Emirates", "+971"),
        ("KW", "الكويت", "Kuwait", "+965"),
        ("QA", "قطر", "Qatar", "+974"),
        ("BH", "البحرين", "Bahrain", "+973"),
        ("OM", "عُمان", "Oman", "+968"),
        ("LB", "لبنان", "Lebanon", "+961"),
        ("SY", "سوريا", "Syria", "+963"),
        ("IQ", "العراق", "Iraq", "+964"),
        ("YE", "اليمن", "Yemen", "+967"),
        ("LY", "ليبيا", "Libya", "+218"),
        ("TR", "تركيا", "Türkiye", "+90"),
        ("US", "الولايات المتحدة", "United States", "+1"),
        ("CA", "كندا", "Canada", "+1"),
        ("GB", "المملكة المتحدة", "United Kingdom", "+44"),
        ("DE", "ألمانيا", "Germany", "+49"),
        ("SE", "السويد", "Sweden", "+46"),
        ("CL", "تشيلي", "Chile", "+56"),
        ("AU", "أستراليا", "Australia", "+61")
    ];
}
