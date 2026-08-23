using FamilyTree.Contracts.FamilyMembers;

namespace FamilyTree.Application.FamilyMembers;

public interface IFamilyMemberService
{
    Task<FamilyMemberResponse> CreateAsync(CreateFamilyMemberRequest request, CancellationToken ct = default);

    /// <summary>Returns null when no such member is visible to the caller's tenant.</summary>
    Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Case-insensitive substring match on name, ordered by (name, id).
    /// Returns an empty page — never an error — for a blank query or one that matches nothing,
    /// so a caller cannot distinguish "no such name here" from "no such name anywhere"
    /// (design spec §4.4).
    /// </summary>
    /// <param name="limit">Clamped to 1..50.</param>
    /// <param name="offset">Negative values are treated as 0.</param>
    Task<FamilyMemberSearchResponse> SearchAsync(
        string query, int limit, int offset, CancellationToken ct = default);

    Task<FamilyMemberResponse> UpdateAsync(
        Guid id, UpdateFamilyMemberRequest request, CancellationToken ct = default);

    /// <summary>
    /// Re-parents a member, or promotes them to first generation with a null parent id.
    /// Throws <c>MOVE_CREATES_CYCLE</c> when the target is the member or one of their
    /// descendants, and <c>MEMBER_NOT_FOUND</c> when either id names nothing visible to the
    /// caller's tenant.
    /// </summary>
    Task<FamilyMemberResponse> MoveAsync(
        Guid id, MoveFamilyMemberRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
