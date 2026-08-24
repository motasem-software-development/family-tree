namespace FamilyTree.Domain.FamilyMembers;

/// <summary>
/// The contact and identification facts of a member, carried as one value so they enter the
/// aggregate through a single parameter rather than four — one edit, one version bump.
///
/// Replace-semantics, like the life details: a null or blank field clears the stored value.
/// That is what makes removing a wrong phone number possible.
/// </summary>
public readonly record struct ContactDetails(
    string? NationalId,
    string? MobileNumber,
    string? WhatsAppNumber,
    int? CountryId)
{
    public static ContactDetails Empty => new(null, null, null, null);
}
