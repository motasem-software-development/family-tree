using FamilyTree.Contracts.FamilyMembers;

namespace FamilyTree.Application.FamilyMembers;

/// <summary>
/// Alive/deceased, or neither. There is no stored Status column — <c>IsDeceased</c> already is
/// the flag specification §13 asks for, and a second representation of the same fact is how two
/// representations drift apart (design spec §2.5).
/// </summary>
public enum MemberStatusFilter
{
    All,
    Alive,
    Deceased
}

/// <summary>
/// The validated form of <see cref="MemberFilterRequest"/>: status parsed, blank strings folded
/// to null. Everything downstream — the SQL, the tree assembly, the export — reads this, so no
/// two callers can parse the wire shape differently.
/// </summary>
public sealed record MemberFilter(
    string? Search,
    MemberStatusFilter Status,
    Guid? BranchId,
    int? Generation,
    int? CountryId,
    Guid? RootId)
{
    public static MemberFilter None { get; } =
        new(null, MemberStatusFilter.All, null, null, null, null);

    /// <summary>
    /// True when nothing is being filtered out.
    ///
    /// <see cref="RootId"/> is excluded on purpose: it changes what branch and generation are
    /// measured from, not which members come back. Counting it would send an unfiltered subtree
    /// view down the filtering path and let the UI tell the user they are filtered when they
    /// are not.
    /// </summary>
    public bool IsEmpty =>
        Search is null
        && Status is MemberStatusFilter.All
        && BranchId is null
        && Generation is null
        && CountryId is null;

    /// <summary>
    /// Returns false only for an unrecognised status, which the caller turns into a 400
    /// <c>FILTER_INVALID_STATUS</c> (design spec §5.1). Every other malformed-looking value is a
    /// filter that matches nothing, which is a legitimate answer rather than an error: an
    /// unknown branch id or a generation nobody sits at returns an empty list.
    /// </summary>
    public static bool TryCreate(MemberFilterRequest request, out MemberFilter filter)
    {
        filter = None;

        if (!TryParseStatus(request.Status, out var status)) return false;

        // Blank and absent must be the same thing: `?search=` and no parameter at all arrive
        // identically, and a whitespace-only term would otherwise become a pattern matching
        // every member whose name contains a space.
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        filter = new MemberFilter(
            search,
            status,
            request.BranchId,
            request.Generation,
            request.CountryId,
            request.RootId);

        return true;
    }

    private static bool TryParseStatus(string? status, out MemberStatusFilter parsed)
    {
        parsed = MemberStatusFilter.All;
        if (string.IsNullOrWhiteSpace(status)) return true;

        var trimmed = status.Trim();

        if (Matches(trimmed, "all")) return true;

        if (Matches(trimmed, "alive"))
        {
            parsed = MemberStatusFilter.Alive;
            return true;
        }

        if (Matches(trimmed, "deceased"))
        {
            parsed = MemberStatusFilter.Deceased;
            return true;
        }

        return false;
    }

    private static bool Matches(string value, string keyword) =>
        string.Equals(value, keyword, StringComparison.OrdinalIgnoreCase);
}
