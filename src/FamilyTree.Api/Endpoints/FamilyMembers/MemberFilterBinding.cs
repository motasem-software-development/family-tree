using FamilyTree.Api.Errors;
using FamilyTree.Application.FamilyMembers;
using FamilyTree.Contracts.FamilyMembers;

namespace FamilyTree.Api.Endpoints.FamilyMembers;

/// <summary>
/// Turns the bound query string into a validated filter, or into the one 400 the filter set can
/// produce.
///
/// Shared by the members list, the tree view, and the Excel export rather than written out at
/// each: three copies of "what does status=dead mean" is three chances for two of them to
/// disagree, and a client must not be able to learn two spellings of the same mistake.
/// </summary>
internal static class MemberFilterBinding
{
    /// <summary>
    /// Design spec §5.1 specifies defaults for ABSENT parameters, not for invalid ones —
    /// following the precedent <c>EXPORT_INVALID_STYLE</c> set. Silently treating
    /// <c>status=dead</c> as "all" would return a 200 carrying a different result than the
    /// caller asked for, with nothing to tell them so.
    ///
    /// Every other filter value that names nothing — an unknown branch, a country that does not
    /// exist, a generation nobody sits at — is a filter matching nothing, and returns an empty
    /// list rather than an error.
    /// </summary>
    public static bool TryBind(MemberFilterRequest request, out MemberFilter filter, out IResult error)
    {
        error = null!;
        if (MemberFilter.TryCreate(request, out filter)) return true;

        error = ProblemResults.Coded(
            StatusCodes.Status400BadRequest,
            "FILTER_INVALID_STATUS",
            "Unknown status filter. Use 'all', 'alive', or 'deceased'.");

        return false;
    }
}
