using FamilyTree.Contracts.Users;

namespace FamilyTree.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns null when no such user is visible to the caller's tenant.</summary>
    Task<UserResponse?> GetAsync(Guid id, CancellationToken ct = default);

    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
}
