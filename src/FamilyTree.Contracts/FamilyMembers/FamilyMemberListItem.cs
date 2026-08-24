namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// One row of the filtered members list: everything <see cref="FamilyMemberResponse"/> carries,
/// plus where the member sits relative to the selected root.
///
/// A separate record rather than three more fields on <see cref="FamilyMemberResponse"/>, because
/// the single-member endpoints have no root to measure from. A <c>Generation</c> that meant "not
/// applicable here" on one endpoint and a real depth on another is the kind of field that gets
/// misread once and then relied on.
///
/// <paramref name="BranchId"/> and <paramref name="BranchName"/> are null for the root member,
/// which specification §21 renders as "Root" — the absence of a branch, not a branch that can be
/// selected. <paramref name="Generation"/> is root-relative, 0 at the root (design spec §1.2);
/// this deliberately differs from the absolute 1-based number the tree view and the PDF caption
/// carry.
///
/// Flat rather than nesting a <c>FamilyMemberResponse</c>: the client renders one table row per
/// item, and the nesting would buy nothing but a level of indirection.
/// </summary>
public sealed record FamilyMemberListItem(
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
    string? CountryCode,
    Guid? BranchId,
    string? BranchName,
    int Generation);
