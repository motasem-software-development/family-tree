namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// A single member as returned by the API. <paramref name="Version"/> must be echoed back on
/// update — it is the optimistic concurrency token (design spec §3.1).
///
/// <paramref name="CountryCode"/> rides along with <paramref name="CountryId"/> so a client can
/// render a flag and a name without joining against the country list it may not have loaded yet.
/// </summary>
public sealed record FamilyMemberResponse(
    Guid Id,
    string Name,
    Guid? ParentId,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateOnly? DateOfBirth,
    DateOnly? DateOfDeath,
    bool IsDeceased,
    string? NationalId,
    string? MobileNumber,
    string? WhatsAppNumber,
    int? CountryId,
    string? CountryCode);
