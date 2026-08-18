using FamilyTree.Contracts.Roles;

namespace FamilyTree.Application.Roles;

public interface IRoleService
{
    Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns null when no such role is visible to the caller's tenant.</summary>
    Task<RoleResponse?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>The global permission catalog. Not tenant-scoped — every tenant sees all codes.</summary>
    Task<IReadOnlyList<PermissionResponse>> ListPermissionsAsync(CancellationToken ct = default);

    Task<RoleResponse> CreateAsync(SaveRoleRequest request, CancellationToken ct = default);

    Task<RoleResponse> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
