using System.Globalization;
using FamilyTree.Domain.FamilyMembers;

namespace FamilyTree.Application.FamilyMembers;

/// <summary>
/// Specification §15's combinability, in one place: a plain AND across the supplied predicates,
/// with a null filter field meaning "no filter" rather than "must be null".
///
/// The client-side twin of the WHERE clause in <c>FamilyMemberQuery</c>. Keeping it here — pure,
/// and free of both EF and SQL — is what lets "what does status=alive mean" be answered once and
/// asserted in milliseconds, rather than being re-derived at each call site.
/// </summary>
public static class MemberFilterPredicate
{
    public static bool Matches(FamilyMember member, MemberPlacement placement, MemberFilter filter) =>
        MatchesSearch(member.Name, filter.Search)
        && MatchesStatus(member.IsDeceased, filter.Status)
        && MatchesBranch(placement.BranchId, filter.BranchId)
        && (filter.Generation is not { } generation || placement.Generation == generation)
        && (filter.CountryId is not { } countryId || member.CountryId == countryId);

    /// <summary>
    /// Name only. A national ID or a phone number must never be reachable from the name box:
    /// they are contact details, and searching them silently would disclose more than the user
    /// asked for.
    ///
    /// Culture-insensitive, case-insensitive substring — the closest match in .NET to the
    /// <c>ILIKE '%term%'</c> the SQL side uses, which is what lets the two agree on the Arabic
    /// corpus.
    /// </summary>
    private static bool MatchesSearch(string name, string? search) =>
        search is null
        || CultureInfo.InvariantCulture.CompareInfo.IndexOf(name, search, CompareOptions.IgnoreCase) >= 0;

    private static bool MatchesStatus(bool isDeceased, MemberStatusFilter status) => status switch
    {
        MemberStatusFilter.Alive => !isDeceased,
        MemberStatusFilter.Deceased => isDeceased,
        _ => true
    };

    /// <summary>
    /// The root has no branch, so a branch filter never matches it — "Root" is a rendering of
    /// the absence of a branch (specification §21), not a value that can be selected. Written
    /// out rather than left to <c>Guid?</c> equality because null == null would otherwise make
    /// a filter for the root's own id match the root.
    /// </summary>
    private static bool MatchesBranch(Guid? branchId, Guid? filterBranchId) =>
        filterBranchId is not { } wanted || (branchId is { } actual && actual == wanted);
}
