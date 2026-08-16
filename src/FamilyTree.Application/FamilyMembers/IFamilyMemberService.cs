using FamilyTree.Contracts.FamilyMembers;

namespace FamilyTree.Application.FamilyMembers;

public interface IFamilyMemberService
{
    Task<FamilyMemberResponse> CreateAsync(CreateFamilyMemberRequest request, CancellationToken ct = default);

    /// <summary>Returns null when no such member is visible to the caller's tenant.</summary>
    Task<FamilyMemberResponse?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<FamilyMemberResponse>> ListAsync(CancellationToken ct = default);

    Task<FamilyMemberResponse> UpdateAsync(
        Guid id, UpdateFamilyMemberRequest request, CancellationToken ct = default);
}
