using System.Text.RegularExpressions;

namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// The contact and identification facts of a member, carried as one value so they enter the
/// aggregate through a single parameter rather than four — one edit, one version bump.
///
/// Replace-semantics, like the life details: a null or blank field clears the stored value.
/// That is what makes removing a wrong phone number possible.
/// </summary>
public readonly partial record struct ContactDetails(
    string? NationalId,
    string? MobileNumber,
    string? WhatsAppNumber,
    int? CountryId)
{
    public static ContactDetails Empty => new(null, null, null, null);

    /// <summary>
    /// Strips the separators people write phone numbers with — and a pasted number arrives
    /// with — before either shape validation (the aggregate) or dial-code comparison (the
    /// service) runs. The single definition here is what keeps those two call sites from
    /// silently disagreeing on what counts as a separator.
    /// </summary>
    public static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : PhoneSeparators().Replace(phone.Trim(), string.Empty);

    [GeneratedRegex(@"[\s\-()]")]
    private static partial Regex PhoneSeparators();
}
