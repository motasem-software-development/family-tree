using System.Text.RegularExpressions;
using FamilyTree.Domain.Common;

namespace FamilyTree.Domain.Countries;

/// <summary>
/// A country of residence. System-level reference data, not tenant-owned — every tenant sees
/// the same list, so this entity carries no TenantId and no global query filter (design §2.1).
///
/// Deliberately NOT an <see cref="Entity"/>: that base supplies a Guid id and created/updated
/// timestamps, and this is a small seeded lookup keyed by an int identity with no edit history
/// worth keeping. The flag emoji is not stored — it is derivable from <see cref="Code"/> by
/// regional-indicator arithmetic, so the client computes it.
/// </summary>
public sealed partial class Country
{
    public const int MaxNameLength = 100;

    private Country() { }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string NameAr { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string DialCode { get; private set; } = null!;

    public static Country Create(string code, string nameAr, string nameEn, string dialCode) =>
        new()
        {
            Code = ValidateCode(code),
            NameAr = ValidateName(nameAr),
            NameEn = ValidateName(nameEn),
            DialCode = ValidateDialCode(dialCode)
        };

    /// <summary>ISO 3166-1 alpha-2, normalized to upper case so a seed list is case-insensitive.</summary>
    private static string ValidateCode(string code)
    {
        var trimmed = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CodePattern().IsMatch(trimmed))
            throw new DomainException(
                "COUNTRY_CODE_INVALID", "Country code must be two letters (ISO 3166-1 alpha-2).");
        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("COUNTRY_NAME_REQUIRED", "Country name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new DomainException(
                "COUNTRY_NAME_TOO_LONG", $"Country name exceeds {MaxNameLength} characters.");
        return trimmed;
    }

    private static string ValidateDialCode(string dialCode)
    {
        var trimmed = dialCode?.Trim() ?? string.Empty;
        if (!DialCodePattern().IsMatch(trimmed))
            throw new DomainException(
                "COUNTRY_DIAL_CODE_INVALID", "Dial code must be '+' followed by 1-4 digits.");
        return trimmed;
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^\+[1-9]\d{0,3}$")]
    private static partial Regex DialCodePattern();
}
