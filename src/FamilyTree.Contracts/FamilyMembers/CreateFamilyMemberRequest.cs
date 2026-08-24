namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// Creates a member. <paramref name="ParentId"/> null means a first-generation member directly
/// under the root family (technical specification §10). The tenant and the family tree are
/// resolved server-side and are never accepted from the client.
///
/// The life details are all optional and default to "living, dates unknown" — the only honest
/// default for the imported tree, which carries names and nothing else. Dates are Gregorian
/// calendar dates (ISO <c>yyyy-MM-dd</c> on the wire); supplying
/// <paramref name="DateOfDeath"/> marks the member deceased regardless of
/// <paramref name="IsDeceased"/>.
/// </summary>
public sealed record CreateFamilyMemberRequest(
    string Name,
    Guid? ParentId,
    DateOnly? DateOfBirth = null,
    DateOnly? DateOfDeath = null,
    bool IsDeceased = false,
    string? NationalId = null,
    string? MobileNumber = null,
    string? WhatsAppNumber = null,
    int? CountryId = null);
