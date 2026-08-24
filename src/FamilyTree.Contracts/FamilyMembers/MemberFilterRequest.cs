namespace FamilyTree.Contracts.FamilyMembers;

/// <summary>
/// The filter set, bound from the query string by every endpoint that filters — the members
/// list, the tree view, and the Excel export. One shared record is what makes specification
/// §15's combinability structural rather than a habit: a filter added here reaches every caller
/// at once, so none can quietly support a subset (design spec §5.1).
///
/// An absent parameter means "no filter". <paramref name="Status"/> is a string rather than an
/// enum because an unrecognised value must be a 400 carrying a code (design spec §5.1) and model
/// binding cannot refuse — <c>MemberFilter</c> in Application is where it becomes typed.
///
/// <paramref name="RootId"/> is not a filter. It selects the root that branch and generation are
/// measured from (design spec §1.3), and it rides in this record rather than as a separate
/// parameter so the export can be handed the page's query string unchanged.
/// </summary>
public sealed record MemberFilterRequest(
    string? Search,
    string? Status,
    Guid? BranchId,
    int? Generation,
    int? CountryId,
    Guid? RootId);
